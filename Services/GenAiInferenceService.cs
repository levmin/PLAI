using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace PLAI.Services
{
    public sealed class GenAiInferenceService : IDisposable
    {
        // ORT GenAI requires an OgaHandle to keep native resources alive.
        // Keep it process-lifetime and initialize lazily to avoid preventing the app from starting
        // on systems missing native prerequisites.
        private static readonly object s_handleLock = new();
        private static OgaHandle? s_handle;

        private Model? _model;
        private Tokenizer? _tokenizer;

        public bool IsLoaded => _model is not null && _tokenizer is not null;

        public async Task LoadModelAsync(string modelFolder, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(modelFolder))
            {
                throw new ArgumentException("Model folder is required.", nameof(modelFolder));
            }

            EnsureHandle();

            await Task.Run(() =>
            {
                DisposeCore();

                _model = new Model(modelFolder);
                _tokenizer = new Tokenizer(_model);
            }, cancellationToken).ConfigureAwait(false);
        }

        private static void EnsureHandle()
        {
            if (s_handle is not null) return;
            lock (s_handleLock)
            {
                if (s_handle is not null) return;
                // If this throws (e.g., missing VC++ runtime / native deps), let the caller handle it.
                s_handle = new OgaHandle();
            }
        }

        public async Task WarmupAsync(CancellationToken cancellationToken)
        {
            if (!IsLoaded) throw new InvalidOperationException("Model is not loaded.");

            // Keep warmup tiny and hidden.
            await GenerateAsyncInternal(
                prompt: "<|user|>Hello<|end|><|assistant|>",
                maxLength: 64,
                doSample: false,
                onTextChunk: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public Task<string> GenerateAsync(
            string prompt,
            int maxLength,
            bool doSample,
            IProgress<string>? onTextChunk,
            CancellationToken cancellationToken)
        {
            if (!IsLoaded) throw new InvalidOperationException("Model is not loaded.");
            if (prompt is null) throw new ArgumentNullException(nameof(prompt));

            return GenerateAsyncInternal(prompt, maxLength, doSample, onTextChunk, cancellationToken);
        }

        private async Task<string> GenerateAsyncInternal(
            string prompt,
            int maxLength,
            bool doSample,
            IProgress<string>? onTextChunk,
            CancellationToken cancellationToken)
        {
            // The GenAI APIs are synchronous; run off the UI thread.
            return await Task.Run(() =>
            {
                if (_model is null || _tokenizer is null)
                {
                    throw new InvalidOperationException("Model is not loaded.");
                }

                using var tokenizerStream = _tokenizer.CreateStream();
                var sequences = _tokenizer.Encode(prompt);

                using var generatorParams = new GeneratorParams(_model);
                generatorParams.SetSearchOption("max_length", maxLength);
                generatorParams.SetSearchOption("do_sample", doSample);

                using var generator = new Generator(_model, generatorParams);
                generator.AppendTokenSequences(sequences);

                var sb = new StringBuilder();

                while (!generator.IsDone())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    generator.GenerateNextToken();

                    var seq = generator.GetSequence(0);
                    if (seq.Length == 0) continue;

                    var tokenId = seq[^1];
                    var text = tokenizerStream.Decode(tokenId);
                    if (!string.IsNullOrEmpty(text))
                    {
                        sb.Append(text);
                        onTextChunk?.Report(text);
                    }
                }

                return sb.ToString();
            }, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            DisposeCore();
            GC.SuppressFinalize(this);
        }

        private void DisposeCore()
        {
            try { _tokenizer?.Dispose(); } catch { }
            _tokenizer = null;

            try { _model?.Dispose(); } catch { }
            _model = null;
        }
    }
}
