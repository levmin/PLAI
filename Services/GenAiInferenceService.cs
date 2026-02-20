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
        // TEMP (v1.2.x): PLAI currently forces Phi-3.5-mini-instruct CPU INT4 AWQ.
        // Keep context length deterministic and avoid relying on GenAI config APIs that vary by version.
        // Phi-3.5 supports a 4K context window.
        private const int DefaultContextLength = 4096;
        private int _contextLength = DefaultContextLength;

        public bool IsLoaded => _model is not null && _tokenizer is not null;

        /// <summary>
        /// The model's context length (max sequence length) as reported by ORT GenAI.
        /// Valid after <see cref="LoadModelAsync"/> completes.
        /// </summary>
        public int ContextLength => _contextLength;

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
                _contextLength = DefaultContextLength;
            }, cancellationToken).ConfigureAwait(false);
        }

        private static void EnsureHandle()
        {
            if (s_handle is not null) return;
            lock (s_handleLock)
            {
                if (s_handle is not null) return;
                // Performance tuning (CPU-only): encourage ORT to use more threads.
                // These environment variables are read by underlying runtimes and are safe no-ops if unsupported.
                try
                {
                    var threads = Math.Max(1, Environment.ProcessorCount);
                    Environment.SetEnvironmentVariable("OMP_NUM_THREADS", threads.ToString());
                    Environment.SetEnvironmentVariable("ORT_NUM_THREADS", threads.ToString());
                    Environment.SetEnvironmentVariable("ORT_INTER_OP_NUM_THREADS", "1");
                }
                catch
                {
                    // Best-effort only.
                }
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

                using var generatorParams = new GeneratorParams(_model);
                // max_length is prompt + generated tokens. If caller passes 0/negative,
                // default to the model's context length.
                var effectiveMax = maxLength > 0 ? maxLength : (_contextLength > 0 ? _contextLength : 4096);
                generatorParams.SetSearchOption("max_length", effectiveMax);
                generatorParams.SetSearchOption("do_sample", doSample);

                // Ensure the prompt leaves some headroom for generation. Without this, long chats
                // can cause overflow errors or produce no output.
                var promptBudget = ComputePromptBudget(effectiveMax);
                var promptToUse = ClipPromptToBudgetDeterministically(prompt, promptBudget);

                // Best-effort: if we can measure token count, tighten the clip until within budget.
                // This remains deterministic (always keeps the tail).
                var sequences = _tokenizer.Encode(promptToUse);
                var tokenCount = TryGetFirstSequenceLength(sequences);
                for (int i = 0; i < 6 && tokenCount > 0 && tokenCount > promptBudget && promptToUse.Length > 256; i++)
                {
                    // Reduce proportionally with a safety factor.
                    var targetChars = (int)Math.Max(256, promptToUse.Length * (double)promptBudget / tokenCount * 0.90);
                    promptToUse = promptToUse.Substring(promptToUse.Length - targetChars, targetChars);
                    sequences = _tokenizer.Encode(promptToUse);
                    tokenCount = TryGetFirstSequenceLength(sequences);
                }

                using var generator = new Generator(_model, generatorParams);
                generator.AppendTokenSequences(sequences);

                var sb = new StringBuilder();

                // Batch UI updates to reduce dispatcher/INotify overhead while still feeling "streamy".
                var chunkBuffer = new StringBuilder();
                long lastFlush = Environment.TickCount64;

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
                        if (onTextChunk is not null)
                        {
                            chunkBuffer.Append(text);

                            var now = Environment.TickCount64;
                            if (chunkBuffer.Length >= 64 || (now - lastFlush) >= 50)
                            {
                                onTextChunk.Report(chunkBuffer.ToString());
                                chunkBuffer.Clear();
                                lastFlush = now;
                            }
                        }
                    }
                }

                // Flush any remaining buffered text.
                if (onTextChunk is not null && chunkBuffer.Length > 0)
                {
                    onTextChunk.Report(chunkBuffer.ToString());
                }

                return sb.ToString();
            }, cancellationToken).ConfigureAwait(false);
        }

        private static int ComputePromptBudget(int effectiveMax)
        {
            // Leave deterministic headroom for the model to respond.
            // Keep it conservative because we no longer cap output tokens.
            // Minimum budget of 64 tokens.
            var reserve = Math.Min(256, Math.Max(128, effectiveMax / 6));
            var budget = effectiveMax - reserve;
            return Math.Max(64, budget);
        }

        private static string ClipPromptToBudgetDeterministically(string prompt, int promptBudgetTokens)
        {
            // Deterministic clipping strategy: keep the tail of the prompt.
            // Token-perfect clipping is not always available via public GenAI APIs,
            // so we use a conservative character-based clip that is stable and safe.

            // Approximate 1 token ~= 4 chars. Use a safety factor.
            var charLimit = (int)Math.Max(512, promptBudgetTokens * 4.0);
            if (prompt.Length <= charLimit) return prompt;

            // Keep the most recent content.
            return prompt.Substring(prompt.Length - charLimit, charLimit);
        }

        private static int TryGetFirstSequenceLength(object sequences)
        {
            try
            {
                var t = sequences.GetType();
                // Common pattern: Sequences.GetSequence(int)
                var getSeq = t.GetMethod("GetSequence", new[] { typeof(int) });
                if (getSeq is null) return 0;
                var seqObj = getSeq.Invoke(sequences, new object[] { 0 });
                if (seqObj is null) return 0;

                if (seqObj is int[] arr) return arr.Length;

                var seqType = seqObj.GetType();
                var lenProp = seqType.GetProperty("Length") ?? seqType.GetProperty("Count");
                if (lenProp is not null && lenProp.PropertyType == typeof(int))
                {
                    return (int)lenProp.GetValue(seqObj)!;
                }

                if (seqObj is System.Collections.ICollection col) return col.Count;
            }
            catch
            {
                // Best-effort only.
            }
            return 0;
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
