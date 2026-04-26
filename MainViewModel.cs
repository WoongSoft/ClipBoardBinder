using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace ID648
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    public sealed class TrackedFileItem
    {
        public TrackedFileItem(string filePath,string displayName)
        {
            FilePath = filePath;
            DisplayName = displayName;
        }

        public string FilePath { get; }

        public string DisplayName { get; }
    }

    public class MainViewModel : ObservableObject,IDisposable
    {
        private const long MaxTextFileSizeBytes = 2 * 1024 * 1024;
        private const int MaxClipboardTextLength = 8 * 1024 * 1024;
        private const string ClipboardOwnershipFormat = "ID648.FileContentClipboard.Managed";
        private static readonly TimeSpan SkippedMessageCooldown = TimeSpan.FromSeconds(2);
        private static readonly Encoding Utf8Encoding = new UTF8Encoding(false,true);
        private static readonly Encoding Utf16LittleEndianEncoding = new UnicodeEncoding(false,true,true);
        private static readonly Encoding Utf16BigEndianEncoding = new UnicodeEncoding(true,true,true);
        private static readonly Encoding Utf32LittleEndianEncoding = new UTF32Encoding(false,true,true);
        private static readonly Encoding Utf32BigEndianEncoding = new UTF32Encoding(true,true,true);

        private sealed class FileClipboardEntry
        {
            public FileClipboardEntry(string filePath,string fileName,string content)
            {
                FilePath = filePath;
                FileName = fileName;
                Content = content;
            }

            public string FilePath { get; }

            public string FileName { get; }

            public string Content { get; }
        }

        private sealed class ClipboardProcessingResult
        {
            public ClipboardProcessingResult(List<string> supportedTextFiles,List<FileClipboardEntry> entries,List<string> skippedItems)
            {
                SupportedTextFiles = supportedTextFiles;
                Entries = entries;
                SkippedItems = skippedItems;
            }

            public List<string> SupportedTextFiles { get; }

            public List<FileClipboardEntry> Entries { get; }

            public List<string> SkippedItems { get; }
        }

        private sealed class ClipboardSnapshot
        {
            public ClipboardSnapshot(List<string> files,bool isManagedByApp)
            {
                Files = files;
                IsManagedByApp = isManagedByApp;
            }

            public List<string> Files { get; }

            public bool IsManagedByApp { get; }
        }

        private readonly List<FileClipboardEntry> _fileClipboardEntries = new List<FileClipboardEntry>();
        private readonly List<string> _clipboardFileDropPaths = new List<string>();
        private readonly SemaphoreSlim _clipboardUpdateLock = new SemaphoreSlim(1,1);
        private string _lastGeneratedClipboardText = string.Empty;
        private string _lastSkippedItemsSignature = string.Empty;
        private DateTime _lastSkippedMessageShownUtc = DateTime.MinValue;
        private bool _isClipboardFailureMessageVisible;
        private bool _disposed;

        private ObservableCollection<TrackedFileItem> _filePaths = new ObservableCollection<TrackedFileItem>();
        public ObservableCollection<TrackedFileItem> FilePaths
        {
            get { return _filePaths; }
            set { SetProperty(ref _filePaths,value); }
        }

        private bool _isClipboardCopyEnabled;
        public bool IsClipboardCopyEnabled
        {
            get { return _isClipboardCopyEnabled; }
            set
            {
                if(SetProperty(ref _isClipboardCopyEnabled,value))
                {
                    RefreshTrackedFilesDisplay();
                    RefreshGeneratedClipboardContent(true);
                }
            }
        }

        private bool _isAlwaysOnTop;
        public bool IsAlwaysOnTop
        {
            get { return _isAlwaysOnTop; }
            set { SetProperty(ref _isAlwaysOnTop,value); }
        }

        private bool _displayFileNamesInClipboard;
        public bool DisplayFileNamesInClipboard
        {
            get { return _displayFileNamesInClipboard; }
            set
            {
                if(SetProperty(ref _displayFileNamesInClipboard,value))
                {
                    RefreshGeneratedClipboardContent();
                }
            }
        }

        private bool _displaySourceFileNamesOnly;
        public bool DisplaySourceFileNamesOnly
        {
            get { return _displaySourceFileNamesOnly; }
            set
            {
                if(SetProperty(ref _displaySourceFileNamesOnly,value))
                {
                    RefreshTrackedFilesDisplay();
                }
            }
        }

        private bool _showNotifications;
        public bool ShowNotifications
        {
            get { return _showNotifications; }
            set { SetProperty(ref _showNotifications,value); }
        }

        private bool _hasFileClipboardData;
        public bool HasFileClipboardData
        {
            get { return _hasFileClipboardData; }
            private set
            {
                if(SetProperty(ref _hasFileClipboardData,value))
                {
                    ClearFileClipboardCommand.NotifyCanExecuteChanged();
                }
            }
        }

        private AppTheme _selectedTheme;
        public AppTheme SelectedTheme
        {
            get { return _selectedTheme; }
            private set
            {
                if(SetProperty(ref _selectedTheme,value))
                {
                    OnPropertyChanged(nameof(IsDarkModeSelected));
                    OnPropertyChanged(nameof(IsLightModeSelected));
                }
            }
        }

        public bool IsDarkModeSelected
        {
            get { return SelectedTheme == AppTheme.Dark; }
            set
            {
                if(value)
                {
                    SelectedTheme = AppTheme.Dark;
                }
            }
        }

        public bool IsLightModeSelected
        {
            get { return SelectedTheme == AppTheme.Light; }
            set
            {
                if(value)
                {
                    SelectedTheme = AppTheme.Light;
                }
            }
        }

        public RelayCommand ClearFileClipboardCommand { get; }
        public RelayCommand<AppTheme?> ChangeThemeCommand { get; }

        public string PreparedFilesSummary => Localization.Format("Files.PreparedCount", FilePaths.Count);

        public MainViewModel()
        {
            FilePaths = new ObservableCollection<TrackedFileItem>();
            FilePaths.CollectionChanged += FilePaths_CollectionChanged;
            ClearFileClipboardCommand = new RelayCommand(ClearFileClipboard,() => HasFileClipboardData);
            ChangeThemeCommand = new RelayCommand<AppTheme?>(ChangeTheme);
            IsClipboardCopyEnabled = true;
            IsAlwaysOnTop = true;
            DisplayFileNamesInClipboard = true;
            DisplaySourceFileNamesOnly = false;
            ShowNotifications = true;
            SelectedTheme = AppTheme.Dark;
            Localization.LanguageChanged += Localization_LanguageChanged;
        }

        private void FilePaths_CollectionChanged(object? sender,NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(PreparedFilesSummary));
        }

        private void Localization_LanguageChanged()
        {
            Application.Current?.Dispatcher.Invoke(() => OnPropertyChanged(nameof(PreparedFilesSummary)));
        }

        private void ChangeTheme(AppTheme? theme)
        {
            if(!theme.HasValue)
            {
                return;
            }

            if(SelectedTheme != theme.Value)
            {
                SelectedTheme = theme.Value;
            }
        }

        public async Task UpdateClipboardContentAsync()
        {
            if(_disposed)
            {
                return;
            }

            await _clipboardUpdateLock.WaitAsync().ConfigureAwait(false);

            try
            {
                if(_disposed)
                {
                    return;
                }

                var clipboardSnapshot = await Application.Current.Dispatcher.InvokeAsync(GetClipboardSnapshot);
                if(clipboardSnapshot.Files.Count == 0)
                {
                    return;
                }

                if(clipboardSnapshot.IsManagedByApp)
                {
                    return;
                }

                var processingResult = await Task.Run(() => ProcessClipboardFiles(clipboardSnapshot.Files)).ConfigureAwait(false);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if(_disposed)
                    {
                        return;
                    }

                    _fileClipboardEntries.Clear();
                    _fileClipboardEntries.AddRange(processingResult.Entries);
                    RefreshTrackedFilesDisplay();

                    _clipboardFileDropPaths.Clear();
                    if(processingResult.Entries.Count > 0)
                    {
                        _clipboardFileDropPaths.AddRange(clipboardSnapshot.Files);
                    }

                    HasFileClipboardData = _clipboardFileDropPaths.Count > 0;

                    if(processingResult.SkippedItems.Count > 0)
                    {
                        ShowSkippedClipboardMessage(processingResult.SkippedItems);
                    }

                    if(HasFileClipboardData)
                    {
                        RefreshGeneratedClipboardContent(true);
                    }
                    else
                    {
                        _lastGeneratedClipboardText = string.Empty;
                    }
                });
            }
            finally
            {
                _clipboardUpdateLock.Release();
            }
        }

        private void UpdateTrackedFiles(IEnumerable<string> filePaths)
        {
            FilePaths.Clear();

            foreach(var filePath in filePaths)
            {
                FilePaths.Add(new TrackedFileItem(filePath,GetTrackedFileDisplayName(filePath)));
            }
        }

        private void RefreshTrackedFilesDisplay()
        {
            if(!IsClipboardCopyEnabled)
            {
                FilePaths.Clear();
                return;
            }

            UpdateTrackedFiles(_fileClipboardEntries.Select(entry => entry.FilePath));
        }

        private string GetTrackedFileDisplayName(string filePath)
        {
            if(!DisplaySourceFileNamesOnly)
            {
                return filePath;
            }

            var fileName = Path.GetFileName(filePath);
            return string.IsNullOrWhiteSpace(fileName) ? filePath : fileName;
        }

        private static ClipboardSnapshot GetClipboardSnapshot()
        {
            try
            {
                if(!Clipboard.ContainsFileDropList())
                {
                    return new ClipboardSnapshot(new List<string>(),false);
                }

                var files = Clipboard.GetFileDropList()
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new ClipboardSnapshot(files,IsClipboardOwnedByThisApp());
            }
            catch(COMException)
            {
                return new ClipboardSnapshot(new List<string>(),false);
            }
            catch(ExternalException)
            {
                return new ClipboardSnapshot(new List<string>(),false);
            }
        }

        private static bool IsClipboardOwnedByThisApp()
        {
            try
            {
                return Clipboard.ContainsData(ClipboardOwnershipFormat);
            }
            catch(COMException)
            {
                return false;
            }
            catch(ExternalException)
            {
                return false;
            }
        }

        private static ClipboardProcessingResult ProcessClipboardFiles(IEnumerable<string> paths)
        {
            var supportedTextFiles = new List<string>();
            var entries = new List<FileClipboardEntry>();
            var skippedItems = new List<string>();

            foreach(var path in paths)
            {
                if(TryCreateFileClipboardEntry(path,out var entry))
                {
                    supportedTextFiles.Add(path);
                    entries.Add(entry);
                }
                else
                {
                    skippedItems.Add(path);
                }
            }

            return new ClipboardProcessingResult(supportedTextFiles,entries,skippedItems);
        }

        private static bool TryCreateFileClipboardEntry(string path,out FileClipboardEntry entry)
        {
            entry = null!;

            if(!TryReadSupportedTextFile(path,out var content))
            {
                return false;
            }

            entry = new FileClipboardEntry(path,Path.GetFileName(path),content);
            return true;
        }

        private static bool TryReadSupportedTextFile(string path,out string content)
        {
            content = string.Empty;

            if(!File.Exists(path))
            {
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                if(fileInfo.Length > MaxTextFileSizeBytes)
                {
                    return false;
                }

                var bytes = File.ReadAllBytes(path);
                if(bytes.Length == 0)
                {
                    return true;
                }

                return TryDecodeTextContent(bytes,out content);
            }
            catch(IOException)
            {
                return false;
            }
            catch(UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool TryDecodeTextContent(byte[] bytes,out string content)
        {
            content = string.Empty;

            if(TryDecodeWithBom(bytes,out content))
            {
                return !ContainsTooManyControlCharacters(content);
            }

            if(TryDecodeWithEncoding(bytes,Utf8Encoding,out content))
            {
                return !ContainsTooManyControlCharacters(content);
            }

            if(TryDecodeUtf16WithoutBom(bytes,out content))
            {
                return !ContainsTooManyControlCharacters(content);
            }

            if(TryDecodeWithActiveCodePage(bytes,out content))
            {
                return !ContainsTooManyControlCharacters(content);
            }

            return false;
        }

        private static bool TryDecodeWithBom(byte[] bytes,out string content)
        {
            content = string.Empty;

            if(HasPrefix(bytes,Encoding.UTF8.GetPreamble()))
            {
                return TryDecodeWithEncoding(bytes,Utf8Encoding,out content,Encoding.UTF8.GetPreamble().Length);
            }

            if(HasPrefix(bytes,Encoding.Unicode.GetPreamble()))
            {
                return TryDecodeWithEncoding(bytes,Utf16LittleEndianEncoding,out content,Encoding.Unicode.GetPreamble().Length);
            }

            if(HasPrefix(bytes,Encoding.BigEndianUnicode.GetPreamble()))
            {
                return TryDecodeWithEncoding(bytes,Utf16BigEndianEncoding,out content,Encoding.BigEndianUnicode.GetPreamble().Length);
            }

            if(HasPrefix(bytes,Encoding.UTF32.GetPreamble()))
            {
                return TryDecodeWithEncoding(bytes,Utf32LittleEndianEncoding,out content,Encoding.UTF32.GetPreamble().Length);
            }

            var utf32BigEndianPreamble = new byte[] { 0x00,0x00,0xFE,0xFF };
            if(HasPrefix(bytes,utf32BigEndianPreamble))
            {
                return TryDecodeWithEncoding(bytes,Utf32BigEndianEncoding,out content,utf32BigEndianPreamble.Length);
            }

            return false;
        }

        private static bool TryDecodeUtf16WithoutBom(byte[] bytes,out string content)
        {
            content = string.Empty;

            if(bytes.Length < 2 || bytes.Length % 2 != 0)
            {
                return false;
            }

            var evenZeroCount = 0;
            var oddZeroCount = 0;
            for(var index = 0; index < bytes.Length; index++)
            {
                if(bytes[index] != 0)
                {
                    continue;
                }

                if(index % 2 == 0)
                {
                    evenZeroCount++;
                }
                else
                {
                    oddZeroCount++;
                }
            }

            var characterCount = bytes.Length / 2;
            var zeroThreshold = Math.Max(2,characterCount / 4);
            if(oddZeroCount >= zeroThreshold && evenZeroCount <= Math.Max(1,characterCount / 20))
            {
                return TryDecodeWithEncoding(bytes,Utf16LittleEndianEncoding,out content);
            }

            if(evenZeroCount >= zeroThreshold && oddZeroCount <= Math.Max(1,characterCount / 20))
            {
                return TryDecodeWithEncoding(bytes,Utf16BigEndianEncoding,out content);
            }

            return false;
        }

        private static bool TryDecodeWithActiveCodePage(byte[] bytes,out string content)
        {
            content = string.Empty;

            try
            {
                var encoding = Encoding.GetEncoding(0,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
                return TryDecodeWithEncoding(bytes,encoding,out content);
            }
            catch(ArgumentException)
            {
                return false;
            }
        }

        private static bool TryDecodeWithEncoding(byte[] bytes,Encoding encoding,out string content,int offset = 0)
        {
            content = string.Empty;

            try
            {
                content = encoding.GetString(bytes,offset,bytes.Length - offset);
                return true;
            }
            catch(DecoderFallbackException)
            {
                return false;
            }
        }

        private static bool HasPrefix(byte[] bytes,byte[] prefix)
        {
            if(prefix.Length == 0 || bytes.Length < prefix.Length)
            {
                return false;
            }

            for(var index = 0; index < prefix.Length; index++)
            {
                if(bytes[index] != prefix[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsTooManyControlCharacters(string content)
        {
            if(string.IsNullOrEmpty(content))
            {
                return false;
            }

            var suspiciousCharacterCount = content.Count(character => char.IsControl(character)
                && character != '\r'
                && character != '\n'
                && character != '\t');

            return suspiciousCharacterCount > Math.Max(2,content.Length / 50);
        }

        private void ShowSkippedClipboardMessage(IEnumerable<string> skippedItems)
        {
            if(!ShowNotifications)
            {
                return;
            }

            var skippedFileNames = skippedItems
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name,StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if(skippedFileNames.Length == 0)
            {
                return;
            }

            var signature = string.Join("|",skippedFileNames);
            var now = DateTime.UtcNow;
            if(signature == _lastSkippedItemsSignature && now - _lastSkippedMessageShownUtc < SkippedMessageCooldown)
            {
                return;
            }

            _lastSkippedItemsSignature = signature;
            _lastSkippedMessageShownUtc = now;

            var message = string.Join(Environment.NewLine,skippedFileNames);
            var detail = string.IsNullOrWhiteSpace(message) ? string.Empty : $"{Environment.NewLine}{Environment.NewLine}{message}";

            var caption = Localization.GetString("App.Title");
            var text = Localization.Format("Skipped.ItemsMessage", detail);
            ThemedMessageDialog.Show(text, caption, MessageBoxImage.Information);
        }

        private void RefreshGeneratedClipboardContent(bool forceClipboardUpdate = false)
        {
            if(!HasFileClipboardData)
            {
                return;
            }

            if(!IsClipboardCopyEnabled)
            {
                _lastGeneratedClipboardText = string.Empty;

                if(forceClipboardUpdate || ShouldUpdateClipboard())
                {
                    TrySetClipboardData(null,_clipboardFileDropPaths);
                }

                return;
            }

            if(_fileClipboardEntries.Count == 0)
            {
                _lastGeneratedClipboardText = string.Empty;

                if(IsClipboardCopyEnabled && (forceClipboardUpdate || ShouldUpdateClipboard()))
                {
                    TrySetClipboardData(null,_clipboardFileDropPaths);
                }

                return;
            }

            if(!TryBuildClipboardText(out var clipboardText))
            {
                _lastGeneratedClipboardText = string.Empty;
                ShowClipboardCapacityMessage();
                return;
            }

            var shouldUpdateClipboard = forceClipboardUpdate || ShouldUpdateClipboard();
            _lastGeneratedClipboardText = clipboardText;

            if(IsClipboardCopyEnabled && shouldUpdateClipboard)
            {
                TrySetClipboardData(clipboardText,_fileClipboardEntries.Select(entry => entry.FilePath));
            }
        }

        private bool TryBuildClipboardText(out string clipboardText)
        {
            var builder = new StringBuilder();

            foreach(var entry in _fileClipboardEntries)
            {
                if(DisplayFileNamesInClipboard)
                {
                    builder.AppendLine(entry.FileName);
                }

                builder.AppendLine(entry.Content);
                builder.AppendLine();

                if(builder.Length > MaxClipboardTextLength)
                {
                    clipboardText = string.Empty;
                    return false;
                }
            }

            clipboardText = builder.ToString();
            return true;
        }

        private bool ShouldUpdateClipboard()
        {
            if(ContainsFileDropListSafely())
            {
                return true;
            }

            var currentClipboardText = GetClipboardTextSafely();
            return !string.IsNullOrEmpty(currentClipboardText) && currentClipboardText == _lastGeneratedClipboardText;
        }

        private void ClearFileClipboard()
        {
            var shouldClearClipboard = ContainsFileDropListSafely() || GetClipboardTextSafely() == _lastGeneratedClipboardText;

            _fileClipboardEntries.Clear();
            _clipboardFileDropPaths.Clear();
            FilePaths.Clear();
            HasFileClipboardData = false;
            _lastGeneratedClipboardText = string.Empty;
            _lastSkippedItemsSignature = string.Empty;
            _lastSkippedMessageShownUtc = DateTime.MinValue;

            if(shouldClearClipboard)
            {
                TryClearClipboard();
            }
        }

        public void RemoveSelectedFileClipboardItems(IEnumerable<string> selectedFilePaths)
        {
            var selectedPaths = selectedFilePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if(selectedPaths.Count == 0 || _fileClipboardEntries.Count == 0)
            {
                return;
            }

            var removedEntryCount = _fileClipboardEntries.RemoveAll(entry => selectedPaths.Contains(entry.FilePath));
            if(removedEntryCount == 0)
            {
                return;
            }

            _clipboardFileDropPaths.RemoveAll(path => selectedPaths.Contains(path));

            for(var index = FilePaths.Count - 1; index >= 0; index--)
            {
                if(selectedPaths.Contains(FilePaths[index].FilePath))
                {
                    FilePaths.RemoveAt(index);
                }
            }

            HasFileClipboardData = _clipboardFileDropPaths.Count > 0;

            if(_fileClipboardEntries.Count > 0)
            {
                RefreshGeneratedClipboardContent(true);
                return;
            }

            if(HasFileClipboardData)
            {
                _lastGeneratedClipboardText = string.Empty;
                TrySetClipboardData(null,_clipboardFileDropPaths);
                return;
            }

            var shouldClearClipboard = ContainsFileDropListSafely() || GetClipboardTextSafely() == _lastGeneratedClipboardText;
            _lastGeneratedClipboardText = string.Empty;

            if(shouldClearClipboard)
            {
                TryClearClipboard();
            }
        }

        private async void TrySetClipboardData(string? clipboardText,IEnumerable<string> filePaths)
        {
            var fileDropList = new StringCollection();
            foreach(var filePath in filePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                fileDropList.Add(filePath);
            }

            for(var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var dataObject = new DataObject();

                    if(!string.IsNullOrEmpty(clipboardText))
                    {
                        dataObject.SetData(DataFormats.UnicodeText,clipboardText);
                        dataObject.SetData(DataFormats.Text,clipboardText);
                    }

                    if(fileDropList.Count > 0)
                    {
                        dataObject.SetFileDropList(fileDropList);
                    }

                    dataObject.SetData(ClipboardOwnershipFormat,true);
                    Clipboard.SetDataObject(dataObject,true);
                    _isClipboardFailureMessageVisible = false;
                    return;
                }
                catch(OutOfMemoryException)
                {
                    ShowClipboardCapacityMessage();
                    return;
                }
                catch(COMException) when(attempt < 2)
                {
                    await Task.Delay(50);
                }
                catch(ExternalException) when(attempt < 2)
                {
                    await Task.Delay(50);
                }
                catch(COMException)
                {
                    ShowClipboardCapacityMessage();
                    return;
                }
                catch(ExternalException)
                {
                    ShowClipboardCapacityMessage();
                    return;
                }
            }
        }

        private void ShowClipboardCapacityMessage()
        {
            if(!ShowNotifications)
            {
                return;
            }

            if(_isClipboardFailureMessageVisible)
            {
                return;
            }

            _isClipboardFailureMessageVisible = true;

            var caption = Localization.GetString("App.Title");
            var text = Localization.GetString("Clipboard.CapacityExceeded");
            ThemedMessageDialog.Show(text, caption, MessageBoxImage.Warning);
        }

        private static bool ContainsFileDropListSafely()
        {
            try
            {
                return Clipboard.ContainsFileDropList();
            }
            catch(COMException)
            {
                return false;
            }
            catch(ExternalException)
            {
                return false;
            }
        }

        private static string GetClipboardTextSafely()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            }
            catch(COMException)
            {
                return string.Empty;
            }
            catch(ExternalException)
            {
                return string.Empty;
            }
        }

        private static void TryClearClipboard()
        {
            try
            {
                Clipboard.Clear();
            }
            catch(COMException)
            {
            }
            catch(ExternalException)
            {
            }
        }

        public void Dispose()
        {
            if(_disposed)
            {
                return;
            }

            _disposed = true;
            FilePaths.CollectionChanged -= FilePaths_CollectionChanged;
            Localization.LanguageChanged -= Localization_LanguageChanged;
            _clipboardUpdateLock.Dispose();
        }
    }
}
