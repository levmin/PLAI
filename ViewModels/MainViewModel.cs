using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PLAI.Models;
using PLAI.Services;

namespace PLAI.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        // TEMPORARY PERFORMANCE OVERRIDE (user-requested):
        // Force a smaller CPU model regardless of detected hardware.
        // Remove once performance work / GPU EP is implemented.
        private const string ForcedModelId = "phi-3_5-mini-instruct-cpu-int4-awq";

        private readonly ModelCatalogService _catalogService = new ModelCatalogService();
        
        private readonly ModelDownloadService _downloadService = new ModelDownloadService();
        // Lazily created so that missing native prerequisites for ORT GenAI don't prevent the app UI from starting.
        private GenAiInferenceService? _inferenceService;
        private readonly ISelectedModelStateStore _stateStore = new PackageLocalSelectedModelStateStore();

        private CancellationTokenSource? _startupCts;
        private CancellationTokenSource? _generationCts;

        private HardwareInfo? _hardwareInfo;
        private ModelDescriptor? _selectedModel;
        private string _detectedHardwareSummary = "Not yet detected.";
        private string _selectionReason = "Not yet selected.";

        private string _statusText = "Idle";
        private bool _isStartupBusy;
        private bool _isGenerating;
        private double _downloadProgressPercent;
        private bool _isDownloadProgressIndeterminate;
        private string _downloadDetail = string.Empty;

        private string _userInputText = string.Empty;

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public HardwareInfo? HardwareInfo
        {
            get => _hardwareInfo;
            private set { _hardwareInfo = value; OnPropertyChanged(); }
        }

        public ModelDescriptor? SelectedModel
        {
            get => _selectedModel;
            private set { _selectedModel = value; OnPropertyChanged(); }
        }

        public string DetectedHardwareSummary
        {
            get => _detectedHardwareSummary;
            private set { _detectedHardwareSummary = value; OnPropertyChanged(); }
        }

        public string SelectionReason
        {
            get => _selectionReason;
            private set { _selectionReason = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsStartupBusy
        {
            get => _isStartupBusy;
            private set
            {
                if (_isStartupBusy != value)
                {
                    _isStartupBusy = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsCancelVisible));
                    OnPropertyChanged(nameof(IsInputEnabled));
                    OnPropertyChanged(nameof(CanSend));
                }
            }
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            private set
            {
                if (_isGenerating != value)
                {
                    _isGenerating = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsCancelVisible));
                    OnPropertyChanged(nameof(IsInputEnabled));
                    OnPropertyChanged(nameof(CanSend));
                }
            }
        }

        public bool IsChatReady { get; private set; }

        public bool IsInputEnabled => IsChatReady && !IsStartupBusy && !IsGenerating;

        public bool CanSend => IsInputEnabled && !string.IsNullOrWhiteSpace(UserInputText);

        public bool IsCancelVisible => IsStartupBusy || IsGenerating;

        public double DownloadProgressPercent
        {
            get => _downloadProgressPercent;
            private set { _downloadProgressPercent = value; OnPropertyChanged(); }
        }

        public bool IsDownloadProgressIndeterminate
        {
            get => _isDownloadProgressIndeterminate;
            private set { _isDownloadProgressIndeterminate = value; OnPropertyChanged(); }
        }

        public string DownloadDetail
        {
            get => _downloadDetail;
            private set { _downloadDetail = value; OnPropertyChanged(); }
        }

        public string UserInputText
        {
            get => _userInputText;
            set
            {
                if (_userInputText != value)
                {
                    _userInputText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanSend));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Performs deterministic detection + selection and reports whether a download is required.
        /// This method shows no UI and performs no network/model work.
        /// </summary>
        public bool PreflightRequiresDownload(IHardwareInfoProvider hardwareProvider)
        {
            RunDetectionAndSelection(hardwareProvider);

            if (SelectedModel is null)
                return false;

            return !_downloadService.IsModelReadyForInference(SelectedModel.Id);
        }

        public async Task StartupAsync(IHardwareInfoProvider hardwareProvider, bool selectionAlreadyDone = false)
        {
            _startupCts?.Cancel();
            _startupCts?.Dispose();
            _startupCts = new CancellationTokenSource();

            var ct = _startupCts.Token;

            IsStartupBusy = true;
            StatusText = "Detecting hardware and selecting model...";
            DownloadProgressPercent = 0;
            IsDownloadProgressIndeterminate = true;
            DownloadDetail = string.Empty;

            PLAI.DownloadWindow? downloadWindow = null;

            try
            {
                await Task.Yield();

                if (!selectionAlreadyDone)
                {
                    RunDetectionAndSelection(hardwareProvider);
                }

                if (SelectedModel is null)
                {
                    StatusText = "No compatible model. Exiting.";
                    MessageBox.Show("No compatible model was found for this system.\n\nPLAI will exit.", "PLAI", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Application.Current.Shutdown();
                    return;
                }

                var modelId = SelectedModel.Id;

                if (!_downloadService.IsModelReadyForInference(modelId))
                {
                    // FRE Step 1: ask permission to download (OK/Cancel). Cancel stops FRE.
                    var modelName = SelectedModel.Name;
                    var answer = MessageBox.Show(
                        $"PLAI needs to download {modelName}.\n\nDo you want to continue?",
                        "PLAI",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Information);

                    if (answer != MessageBoxResult.OK)
                    {
                        StatusText = "Cancelled.";
                        Application.Current.Shutdown();
                        return;
                    }

                    // FRE Step 2: show a dedicated download window with progress.
                    StatusText = $"Downloading {modelName}...";
                    IsDownloadProgressIndeterminate = true;
                    DownloadProgressPercent = 0;
                    DownloadDetail = string.Empty;

                    downloadWindow = new PLAI.DownloadWindow(this, modelName);
                    downloadWindow.Show();

                    var progress = new Progress<ModelDownloadProgress>(p =>
                    {
                        DownloadDetail = $"{p.Stage}: {p.CurrentFile ?? string.Empty}".Trim();

                        var frac = p.GetOverallFraction();
                        if (frac.HasValue)
                        {
                            IsDownloadProgressIndeterminate = false;
                            DownloadProgressPercent = Math.Clamp(frac.Value * 100.0, 0.0, 100.0);
                        }
                        else
                        {
                            IsDownloadProgressIndeterminate = true;
                            DownloadProgressPercent = 0;
                        }
                    });

                    var ok = await _downloadService.EnsureModelDownloadedAsync(SelectedModel, progress, ct).ConfigureAwait(true);
                    if (!ok)
                    {
                        // Deterministic cleanup: clear persisted selection and exit.
                        _stateStore.Clear();

                        if (ct.IsCancellationRequested)
                        {
                            StatusText = "Cancelled.";
                            Application.Current.Shutdown();
                            return;
                        }

                        StatusText = "Download failed. Exiting.";
                        MessageBox.Show("Model download did not complete.\n\nPLAI will exit.", "PLAI", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Application.Current.Shutdown();
                        return;
                    }
                }

                // Load + warmup.
                StatusText = "Loading model...";
                var modelFolder = ModelStoragePaths.GetModelFolderPath(modelId);
                _inferenceService ??= new GenAiInferenceService();
                await _inferenceService.LoadModelAsync(modelFolder, ct).ConfigureAwait(true);

                StatusText = "Warming up model...";
                await _inferenceService.WarmupAsync(ct).ConfigureAwait(true);

                StatusText = "Ready.";
                IsChatReady = true;
                OnPropertyChanged(nameof(IsChatReady));
                OnPropertyChanged(nameof(IsInputEnabled));
                OnPropertyChanged(nameof(CanSend));
            }
            catch (OperationCanceledException)
            {
                // Deterministic cleanup on user cancel.
                if (SelectedModel is not null)
                {
                    _downloadService.CleanupIncompleteModelArtifacts(SelectedModel.Id);
                }
                _stateStore.Clear();
                StatusText = "Cancelled. Exiting.";
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                try { AppLogger.Error($"Startup failed: {ex.Message}"); } catch { }

                if (SelectedModel is not null)
                {
                    _downloadService.CleanupIncompleteModelArtifacts(SelectedModel.Id);
                }
                _stateStore.Clear();

                StatusText = "Startup failed. Exiting.";
                MessageBox.Show($"Startup failed:\n\n{ex.Message}\n\nPLAI will exit.", "PLAI", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
            finally
            {
                try { downloadWindow?.CloseFromCode(); } catch { }

                IsStartupBusy = false;
            }
        }

        public void CancelCurrentOperation()
        {
            try
            {
                _generationCts?.Cancel();
                _startupCts?.Cancel();
            }
            catch { }
        }

        public void Shutdown()
        {
            try { CancelCurrentOperation(); } catch { }

            try { _generationCts?.Dispose(); } catch { }
            _generationCts = null;

            try { _startupCts?.Dispose(); } catch { }
            _startupCts = null;

            try { _inferenceService?.Dispose(); } catch { }
            _inferenceService = null;
        }


        public async Task SendUserMessageAsync()
        {
            if (!IsInputEnabled) return;

            var text = (UserInputText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            UserInputText = string.Empty;

            var userMsg = new ChatMessage(ChatRole.User, text);
            Messages.Add(userMsg);

            var assistantMsg = new ChatMessage(ChatRole.Assistant, string.Empty);
            Messages.Add(assistantMsg);

            IsGenerating = true;

            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generationCts = new CancellationTokenSource();

            try
            {
                var prompt = BuildPhiPromptFromConversation(Messages, ignoreLastAssistantIfEmpty: true);

                // Build the assistant text efficiently (avoid repeated string concatenation).
                var assistantBuffer = new StringBuilder();

                var chunkProgress = new Progress<string>(chunk =>
                {
                    assistantBuffer.Append(chunk);
                    assistantMsg.Content = assistantBuffer.ToString();
                });

                // Keep deterministic generation for now (greedy).
                if (_inferenceService is null)
                {
                    throw new InvalidOperationException("Model is not loaded.");
                }

                var svc = _inferenceService ?? throw new InvalidOperationException("Inference service is not initialized.");

                _ = await svc.GenerateAsync(
                    prompt: prompt,
                    // Use the model's full context window (no artificial output cap).
                    // GenAiInferenceService will default to the model's ContextLength when 0 is passed.
                    maxLength: 0,
                    doSample: false,
                    onTextChunk: chunkProgress,
                    cancellationToken: _generationCts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Keep partial text; do not inject cancellation markers into the transcript.
            }
            catch (Exception ex)
            {
                assistantMsg.Content += $"\n\n[Error: {ex.Message}]";
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private void RunDetectionAndSelection(IHardwareInfoProvider provider)
        {
            // If persisted state exists and model is present, skip detection/selection (v1 behavior).
            if (TryRestoreSelection())
            {
                DetectedHardwareSummary = "Hardware detection skipped (restored selection).";
                return;
            }

            HardwareInfo = provider.GetHardwareInfo();

            DetectedHardwareSummary =
                $"RAM: {HardwareInfo.RamGb:0.0} GB (known: {HardwareInfo.IsRamKnown}), " +
                $"VRAM: {(HardwareInfo.IsVramKnown ? HardwareInfo.VramGb.ToString("0.0") : "N/A")} GB (known: {HardwareInfo.IsVramKnown}), " +
                $"Discrete GPU: {HardwareInfo.HasDiscreteGpu}";

            var capabilities = new HardwareCapabilities
            {
                AvailableRamGb = HardwareInfo.RamGb,
                AvailableVramGb = HardwareInfo.IsVramKnown ? HardwareInfo.VramGb : 0.0
            };

            var models = _catalogService.GetAllModels();
            var chosen = ModelSelectionService.ChooseBestModel(capabilities, models);

            SelectedModel = chosen;

            if (chosen is null)
            {
                SelectionReason = "No suitable model found for detected hardware.";
            }
            else
            {
                try { AppLogger.Info($"Selected model {chosen.Id}"); } catch { }
                SelectionReason =
                    "Selected by temporary forced model override (ignoring detected hardware).";

                _stateStore.SaveSelectedModelId(chosen.Id);
            }
        }


        private bool TryRestoreSelection()
        {
            if (!_stateStore.TryLoadSelectedModelId(out var savedId) || string.IsNullOrWhiteSpace(savedId))
                return false;

            // TEMPORARY OVERRIDE: ignore any previously persisted selection that is not the forced model.
            if (!string.Equals(savedId, ForcedModelId, StringComparison.Ordinal))
            {
                _stateStore.Clear();
                return false;
            }

            // Preserve v1 behavior: if the alias file is missing, treat as no state.
            if (!_downloadService.IsModelComplete(savedId))
            {
                _stateStore.Clear();
                return false;
            }

            var models = _catalogService.GetAllModels();
            ModelDescriptor? restored = null;
            foreach (var m in models)
            {
                if (string.Equals(m.Id, savedId, StringComparison.Ordinal))
                {
                    restored = m;
                    break;
                }
            }

            if (restored is null)
            {
                _stateStore.Clear();
                return false;
            }

            SelectedModel = restored;
            SelectionReason = "Restored previous selection (temporary forced model)";

            return true;
        }
        private static string BuildPhiPromptFromConversation(ObservableCollection<ChatMessage> messages, bool ignoreLastAssistantIfEmpty)
        {
            // Phi chat format (minimal):
            // <|user|>...<|end|><|assistant|>...<|end|> ... <|assistant|>
            //
            // During generation we keep a placeholder assistant message in the list; we must not serialize it as
            // "<|assistant|><|end|>" or the model will see a completed assistant turn.

            var sb = new StringBuilder();

            int count = messages.Count;
            int lastIndex = count - 1;

            for (int i = 0; i < count; i++)
            {
                var m = messages[i];

                if (ignoreLastAssistantIfEmpty && i == lastIndex && m.Role == ChatRole.Assistant && string.IsNullOrWhiteSpace(m.Content))
                {
                    // Skip placeholder.
                    continue;
                }

                if (m.Role == ChatRole.User)
                {
                    sb.Append("<|user|>");
                    sb.Append(m.Content);
                    sb.Append("<|end|>");
                }
                else if (m.Role == ChatRole.Assistant)
                {
                    sb.Append("<|assistant|>");
                    sb.Append(m.Content);
                    sb.Append("<|end|>");
                }
            }

            sb.Append("<|assistant|>");
            return sb.ToString();
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
