﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Filtration.Common.Services;
using Filtration.Interface;
using Filtration.ObjectModel.Enums;
using Filtration.ObjectModel.ThemeEditor;
using Filtration.Parser.Interface.Services;
using Filtration.Repositories;
using Filtration.Services;
using Filtration.ThemeEditor.Messages;
using Filtration.ThemeEditor.Providers;
using Filtration.ThemeEditor.Services;
using Filtration.ThemeEditor.ViewModels;
using Filtration.Views;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Messaging;
using NLog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Filtration.ViewModels
{
    internal interface IMainWindowViewModel
    {
        RelayCommand OpenScriptCommand { get; }
        RelayCommand NewScriptCommand { get; }
        Task<bool> CloseAllDocumentsAsync();
        Task OpenDroppedFilesAsync(List<string> filenames);
    }

    internal class MainWindowViewModel : ViewModelBase, IMainWindowViewModel
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IItemFilterScriptRepository _itemFilterScriptRepository;
        private readonly IItemFilterScriptTranslator _itemFilterScriptTranslator;
        private readonly IReplaceColorsViewModel _replaceColorsViewModel;
        private readonly IAvalonDockWorkspaceViewModel _avalonDockWorkspaceViewModel;
        private readonly IThemeProvider _themeProvider;
        private readonly IThemeService _themeService;
        private readonly IMessageBoxService _messageBoxService;
        private readonly IClipboardService _clipboardService;
        private bool _showLoadingBanner;

        public MainWindowViewModel(IItemFilterScriptRepository itemFilterScriptRepository,
                                   IItemFilterScriptTranslator itemFilterScriptTranslator,
                                   IReplaceColorsViewModel replaceColorsViewModel,
                                   IAvalonDockWorkspaceViewModel avalonDockWorkspaceViewModel,
                                   ISettingsPageViewModel settingsPageViewModel,
                                   IThemeProvider themeProvider,
                                   IThemeService themeService,
                                   IMessageBoxService messageBoxService,
                                   IClipboardService clipboardService)
        {
            _itemFilterScriptRepository = itemFilterScriptRepository;
            _itemFilterScriptTranslator = itemFilterScriptTranslator;
            _replaceColorsViewModel = replaceColorsViewModel;
            _avalonDockWorkspaceViewModel = avalonDockWorkspaceViewModel;
            SettingsPageViewModel = settingsPageViewModel;
            _themeProvider = themeProvider;
            _themeService = themeService;
            _messageBoxService = messageBoxService;
            _clipboardService = clipboardService;

            NewScriptCommand = new RelayCommand(OnNewScriptCommand);
            CopyScriptCommand = new RelayCommand(OnCopyScriptCommand, () => ActiveDocumentIsScript);
            OpenScriptCommand = new RelayCommand(async () => await OnOpenScriptCommand());
            OpenThemeCommand = new RelayCommand(async () => await OnOpenThemeCommandAsync());

            SaveCommand = new RelayCommand(async () => await OnSaveDocumentCommandAsync(), ActiveDocumentIsEditable);
            SaveAsCommand = new RelayCommand(async () => await OnSaveAsCommandAsync(), ActiveDocumentIsEditable);
            CloseCommand = new RelayCommand(OnCloseDocumentCommand, ActiveDocumentIsEditable);

            CopyBlockCommand = new RelayCommand(OnCopyBlockCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedBlock);
            CopyBlockStyleCommand = new RelayCommand(OnCopyBlockStyleCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedBlock);
            PasteCommand = new RelayCommand(OnPasteCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedBlock);
            PasteBlockStyleCommand = new RelayCommand(OnPasteBlockStyleCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedBlock);

            // TODO: Only enabled if undo/redo available
            UndoCommand = new RelayCommand(OnUndoCommand, () => ActiveDocumentIsScript);
            RedoCommand = new RelayCommand(OnRedoCommand, () => ActiveDocumentIsScript);


            MoveBlockUpCommand = new RelayCommand(OnMoveBlockUpCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedBlock);
            MoveBlockDownCommand = new RelayCommand(OnMoveBlockDownCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedBlock);
            MoveBlockToTopCommand = new RelayCommand(OnMoveBlockToTopCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedBlock);
            MoveBlockToBottomCommand = new RelayCommand(OnMoveBlockToBottomCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedBlock);

            AddBlockCommand = new RelayCommand(OnAddBlockCommand, () => ActiveDocumentIsScript);
            AddSectionCommand = new RelayCommand(OnAddSectionCommand, () => ActiveDocumentIsScript);
            DeleteBlockCommand = new RelayCommand(OnDeleteBlockCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedBlock);
            DisableBlockCommand = new RelayCommand(OnDisableBlockCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedEnabledBlock);
            EnableBlockCommand = new RelayCommand(OnEnableBlockCommand, () => ActiveDocumentIsScript && ActiveScriptHasSelectedDisabledBlock);
            OpenAboutWindowCommand = new RelayCommand(OnOpenAboutWindowCommand);
            ReplaceColorsCommand = new RelayCommand(OnReplaceColorsCommand, () => ActiveDocumentIsScript);

            CreateThemeCommand = new RelayCommand(OnCreateThemeCommand, () => ActiveDocumentIsScript);
            ApplyThemeToScriptCommand = new RelayCommand(async () => await OnApplyThemeToScriptCommandAsync(), () => ActiveDocumentIsScript);
            EditMasterThemeCommand = new RelayCommand(OnEditMasterThemeCommand, () => ActiveDocumentIsScript);

            AddTextColorThemeComponentCommand = new RelayCommand(OnAddTextColorThemeComponentCommand, () => ActiveDocumentIsTheme && ActiveThemeIsEditable);
            AddBackgroundColorThemeComponentCommand = new RelayCommand(OnAddBackgroundColorThemeComponentCommand, () => ActiveDocumentIsTheme && ActiveThemeIsEditable);
            AddBorderColorThemeComponentCommand = new RelayCommand(OnAddBorderColorThemeComponentCommand, () => ActiveDocumentIsTheme && ActiveThemeIsEditable);
            DeleteThemeComponentCommand = new RelayCommand(OnDeleteThemeComponentCommand, () => ActiveDocumentIsTheme && ActiveThemeIsEditable && _avalonDockWorkspaceViewModel.ActiveThemeViewModel.SelectedThemeComponent != null);

            ExpandAllBlocksCommand = new RelayCommand(OnExpandAllBlocksCommand, () => ActiveDocumentIsScript);
            CollapseAllBlocksCommand = new RelayCommand(OnCollapseAllBlocksCommand, () => ActiveDocumentIsScript);

            ToggleShowAdvancedCommand = new RelayCommand<bool>(OnToggleShowAdvancedCommand, s => ActiveDocumentIsScript);
            ClearFiltersCommand = new RelayCommand(OnClearFiltersCommand, () => ActiveDocumentIsScript);

            if (string.IsNullOrEmpty(_itemFilterScriptRepository.GetItemFilterScriptDirectory()))
            {
                SetItemFilterScriptDirectory();
            }

            var icon = new BitmapImage();
            icon.BeginInit();
            icon.UriSource = new Uri("pack://application:,,,/Filtration;component/Resources/Icons/filtration_icon.png");
            icon.EndInit();
            Icon = icon;

            Messenger.Default.Register<ThemeClosedMessage>(this, message =>
            {
                if (message.ClosedViewModel == null) return;
                AvalonDockWorkspaceViewModel.CloseDocument(message.ClosedViewModel);
            });

            Messenger.Default.Register<NotificationMessage>(this, message =>
            {
                switch (message.Notification)
                {
                    case "ActiveDocumentChanged":
                    {
                        CopyScriptCommand.RaiseCanExecuteChanged();
                        SaveCommand.RaiseCanExecuteChanged();
                        SaveAsCommand.RaiseCanExecuteChanged();
                        CloseCommand.RaiseCanExecuteChanged();
                        CopyBlockCommand.RaiseCanExecuteChanged();
                        PasteCommand.RaiseCanExecuteChanged();
                        ReplaceColorsCommand.RaiseCanExecuteChanged();
                        ApplyThemeToScriptCommand.RaiseCanExecuteChanged();
                        EditMasterThemeCommand.RaiseCanExecuteChanged();
                        CreateThemeCommand.RaiseCanExecuteChanged();
                        RaisePropertyChanged("ActiveDocumentIsScript");
                        RaisePropertyChanged("ActiveDocumentIsTheme");
                        RaisePropertyChanged("ShowAdvancedStatus");
                        break;
                    }
                    case "NewScript":
                    {
                        OnNewScriptCommand();
                        break;
                    }
                    case "OpenScript":
                    {
#pragma warning disable 4014
                        OnOpenScriptCommand();
#pragma warning restore 4014
                        break;
                    }
                    case "ShowLoadingBanner":
                    {
                        ShowLoadingBanner = true;
                        break;
                    }
                    case "HideLoadingBanner":
                    {
                        ShowLoadingBanner = false;
                        break;
                    }
                }
            });
        }

        public RelayCommand OpenScriptCommand { get; }
        public RelayCommand OpenThemeCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand SaveAsCommand { get; }
        public RelayCommand CopyBlockCommand { get; }
        public RelayCommand CopyBlockStyleCommand { get; }
        public RelayCommand PasteCommand { get; }
        public RelayCommand PasteBlockStyleCommand { get; }
        public RelayCommand CopyScriptCommand { get; }
        public RelayCommand NewScriptCommand { get; }
        public RelayCommand CloseCommand { get; }
        public RelayCommand OpenAboutWindowCommand { get; }
        public RelayCommand ReplaceColorsCommand { get; }

        public RelayCommand UndoCommand { get;}
        public RelayCommand RedoCommand { get; }

        public RelayCommand EditMasterThemeCommand { get; }
        public RelayCommand CreateThemeCommand { get; }
        public RelayCommand ApplyThemeToScriptCommand { get; }

        public RelayCommand AddTextColorThemeComponentCommand { get; }
        public RelayCommand AddBackgroundColorThemeComponentCommand { get; }
        public RelayCommand AddBorderColorThemeComponentCommand { get; }
        public RelayCommand DeleteThemeComponentCommand { get; }

        public RelayCommand AddBlockCommand { get; }
        public RelayCommand AddSectionCommand { get; }
        public RelayCommand DeleteBlockCommand { get; }
        public RelayCommand DisableBlockCommand { get; }
        public RelayCommand EnableBlockCommand { get; }

        public RelayCommand MoveBlockUpCommand { get; }
        public RelayCommand MoveBlockDownCommand { get; }
        public RelayCommand MoveBlockToTopCommand { get; }
        public RelayCommand MoveBlockToBottomCommand { get; }

        public RelayCommand ExpandAllBlocksCommand { get; }
        public RelayCommand CollapseAllBlocksCommand { get; }

        public RelayCommand<bool> ToggleShowAdvancedCommand { get; }
        public RelayCommand ClearFiltersCommand { get; }

        public ImageSource Icon { get; private set; }

        public IAvalonDockWorkspaceViewModel AvalonDockWorkspaceViewModel => _avalonDockWorkspaceViewModel;
        public ISettingsPageViewModel SettingsPageViewModel { get; }

        public string WindowTitle
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                return "Filtration v" + fvi.FileMajorPart + "." +  fvi.FileMinorPart;
            }
        }

        public bool ShowLoadingBanner
        {
            get { return _showLoadingBanner; }
            private set
            {
                _showLoadingBanner = value;
                RaisePropertyChanged();
            }
        }

        public bool ActiveDocumentIsScript => _avalonDockWorkspaceViewModel.ActiveDocument != null && _avalonDockWorkspaceViewModel.ActiveDocument.IsScript;

        public bool ActiveDocumentIsTheme => _avalonDockWorkspaceViewModel.ActiveDocument!= null && _avalonDockWorkspaceViewModel.ActiveDocument.IsTheme;

        public bool ActiveScriptHasSelectedBlock => AvalonDockWorkspaceViewModel.ActiveScriptViewModel.SelectedBlockViewModel != null;

        public bool ActiveScriptHasSelectedEnabledBlock => AvalonDockWorkspaceViewModel.ActiveScriptViewModel.HasSelectedEnabledBlock();

        public bool ActiveScriptHasSelectedDisabledBlock => AvalonDockWorkspaceViewModel.ActiveScriptViewModel.HasSelectedDisabledBlock();

        public bool ActiveThemeIsEditable => AvalonDockWorkspaceViewModel.ActiveThemeViewModel.IsMasterTheme;

        private bool ActiveDocumentIsEditable()
        {
            return AvalonDockWorkspaceViewModel.ActiveDocument is IEditableDocument;
        }

        public bool ShowAdvancedStatus => ActiveDocumentIsScript && _avalonDockWorkspaceViewModel.ActiveScriptViewModel.ShowAdvanced;

        public async Task OpenDroppedFilesAsync(List<string> filenames)
        {
            foreach (var filename in filenames)
            {
                var extension = Path.GetExtension(filename);
                if (extension == null) continue;

                switch (extension.ToUpperInvariant())
                {
                    case ".FILTER":
                    {
                        await LoadScriptAsync(filename);
                        break;
                    }
                    case ".FILTERTHEME":
                    {
                        await LoadThemeAsync(filename);
                        break;
                    }
                }
            }
        }

        private void OnCreateThemeCommand()
        {
            var themeViewModel = _themeProvider.NewThemeForScript(AvalonDockWorkspaceViewModel.ActiveScriptViewModel.Script);
            OpenTheme(themeViewModel);
        }

        private void OnEditMasterThemeCommand()
        {
            var openMasterThemForScript =
                AvalonDockWorkspaceViewModel.OpenMasterThemeForScript(AvalonDockWorkspaceViewModel.ActiveScriptViewModel);

            if (openMasterThemForScript != null)
            {
                AvalonDockWorkspaceViewModel.SwitchActiveDocument(openMasterThemForScript);
            }
            else
            {
                var themeViewModel =
                    _themeProvider.MasterThemeForScript(AvalonDockWorkspaceViewModel.ActiveScriptViewModel.Script);
                OpenTheme(themeViewModel);
            }
        }
        
        private void OpenTheme(IThemeEditorViewModel themeEditorViewModel)
        {
            if (AvalonDockWorkspaceViewModel.OpenDocuments.Contains(themeEditorViewModel))
            {
                AvalonDockWorkspaceViewModel.SwitchActiveDocument(themeEditorViewModel);
            }
            else
            {
                AvalonDockWorkspaceViewModel.AddDocument(themeEditorViewModel);
            }
        }

        private void OnOpenAboutWindowCommand()
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.ShowDialog();
        }

        private async Task OnOpenScriptCommand()
        {
            var filePath = ShowOpenScriptDialog();

            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            await LoadScriptAsync(filePath); // TODO: fix crash
        }

        private async Task LoadScriptAsync(string scriptFilename)
        {
            IItemFilterScriptViewModel loadedViewModel;

            Messenger.Default.Send(new NotificationMessage("ShowLoadingBanner"));
            try
            {
                loadedViewModel = await _itemFilterScriptRepository.LoadScriptFromFileAsync(scriptFilename);
            }
            catch (Exception e)
            {
                Logger.Error(e);
                _messageBoxService.Show("过滤读取错误", "过滤读取错误 - " + e.Message,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            finally
            {
                Messenger.Default.Send(new NotificationMessage("HideLoadingBanner"));
            }

            _avalonDockWorkspaceViewModel.AddDocument(loadedViewModel);
        }

        private async Task OnOpenThemeCommandAsync()
        {
            var filePath = ShowOpenThemeDialog();
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            await LoadThemeAsync(filePath);
        }

        private async Task LoadThemeAsync(string themeFilename)
        {
            IThemeEditorViewModel loadedViewModel;

            try
            {
                loadedViewModel = await _themeProvider.LoadThemeFromFile(themeFilename);
            }
            catch (Exception e)
            {
                Logger.Error(e);
                _messageBoxService.Show("样式读取错误", "样式读取错误 - " + e.Message,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _avalonDockWorkspaceViewModel.AddDocument(loadedViewModel);
        }

        private async Task OnApplyThemeToScriptCommandAsync()
        {
            var filePath = ShowOpenThemeDialog();
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            Theme loadedTheme;

            try
            {
                loadedTheme = await _themeProvider.LoadThemeModelFromFile(filePath);
            }
            catch (IOException e)
            {
                if (Logger.IsErrorEnabled)
                {
                    Logger.Error(e);
                }
                _messageBoxService.Show("样式读取错误", "样式读取错误 - " + e.Message,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var result = _messageBoxService.Show("注意",
                "请确认使用样式替换当前过滤", MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }

            _themeService.ApplyThemeToScript(loadedTheme, AvalonDockWorkspaceViewModel.ActiveScriptViewModel.Script);
            AvalonDockWorkspaceViewModel.ActiveScriptViewModel.SetDirtyFlag();
        }

        private string ShowOpenScriptDialog()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Filter Files (*.filter)|*.filter|All Files (*.*)|*.*",
                InitialDirectory = _itemFilterScriptRepository.GetItemFilterScriptDirectory()
            };

            return openFileDialog.ShowDialog() != true ? string.Empty : openFileDialog.FileName;
        }

        private string ShowOpenThemeDialog()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Filter Theme Files (*.filtertheme)|*.filtertheme|All Files (*.*)|*.*",
                InitialDirectory = _itemFilterScriptRepository.GetItemFilterScriptDirectory()
            };

            return openFileDialog.ShowDialog() != true ? string.Empty : openFileDialog.FileName;
        }

        private void SetItemFilterScriptDirectory()
        {
            var dlg = new FolderBrowserDialog
            {
                Description = @"选择你的POE文档路径, 通常在 我的文档\My Games",
                ShowNewFolderButton = false
            };
            var result = dlg.ShowDialog();

            if (result == DialogResult.OK)
            {
                _itemFilterScriptRepository.SetItemFilterScriptDirectory(dlg.SelectedPath);
            }
        }

        private async Task OnSaveDocumentCommandAsync()
        {
            await ((IEditableDocument)_avalonDockWorkspaceViewModel.ActiveDocument).SaveAsync();
        }

        private async Task OnSaveAsCommandAsync()
        {
            await ((IEditableDocument)_avalonDockWorkspaceViewModel.ActiveDocument).SaveAsAsync();
        }

        private void OnReplaceColorsCommand()
        {
            _replaceColorsViewModel.Initialise(_avalonDockWorkspaceViewModel.ActiveScriptViewModel.Script);
            var replaceColorsWindow = new ReplaceColorsWindow {DataContext = _replaceColorsViewModel};
            replaceColorsWindow.ShowDialog();
        }

        private void OnCopyScriptCommand()
        {
            try
            {

                _clipboardService.SetClipboardText(
                    _itemFilterScriptTranslator.TranslateItemFilterScriptToString(
                        _avalonDockWorkspaceViewModel.ActiveScriptViewModel.Script));
            }
            catch
            {
                _messageBoxService.Show("剪贴板错误", "不能访问剪贴板, 复制没有完成",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCopyBlockCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.CopyBlockCommand.Execute(null);
        }

        private void OnCopyBlockStyleCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.CopyBlockStyleCommand.Execute(null);
        }

        private void OnPasteCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.PasteBlockCommand.Execute(null);
        }

        private void OnPasteBlockStyleCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.PasteBlockStyleCommand.Execute(null);
        }

        private void OnUndoCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.Script.CommandManager.Undo();
        }

        private void OnRedoCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.Script.CommandManager.Redo();
        }

        private void OnNewScriptCommand()
        {
            var newViewModel = _itemFilterScriptRepository.NewScript();
            _avalonDockWorkspaceViewModel.AddDocument(newViewModel);
        }

        private void OnCloseDocumentCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveDocument.Close();
        }

        private void OnMoveBlockUpCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.MoveBlockUpCommand.Execute(null);
        }

        private void OnMoveBlockDownCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.MoveBlockDownCommand.Execute(null);
        }

        private void OnMoveBlockToTopCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.MoveBlockToTopCommand.Execute(null);
        }

        private void OnMoveBlockToBottomCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.MoveBlockToBottomCommand.Execute(null);
        }

        private void OnAddBlockCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.AddBlockCommand.Execute(null);
        }

        private void OnAddSectionCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.AddSectionCommand.Execute(null);
        }

        private void OnDeleteBlockCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.DeleteBlockCommand.Execute(null);
        }

        private void OnDisableBlockCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.DisableBlockCommand.Execute(null);
        }

        private void OnEnableBlockCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.EnableBlockCommand.Execute(null);
        }

        private void OnExpandAllBlocksCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.ExpandAllBlocksCommand.Execute(null);
        }

        private void OnCollapseAllBlocksCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.CollapseAllBlocksCommand.Execute(null);
        }

        private void OnToggleShowAdvancedCommand(bool showAdvanced)
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.ToggleShowAdvancedCommand.Execute(showAdvanced);
        }

        private void OnClearFiltersCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveScriptViewModel.ClearFilterCommand.Execute(null);
        }

        private void OnAddTextColorThemeComponentCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveThemeViewModel.AddThemeComponentCommand.Execute(ThemeComponentType.TextColor);
        }

        private void OnAddBackgroundColorThemeComponentCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveThemeViewModel.AddThemeComponentCommand.Execute(ThemeComponentType.BackgroundColor);
        }

        private void OnAddBorderColorThemeComponentCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveThemeViewModel.AddThemeComponentCommand.Execute(ThemeComponentType.BorderColor);
        }

        private void OnDeleteThemeComponentCommand()
        {
            _avalonDockWorkspaceViewModel.ActiveThemeViewModel.DeleteThemeComponentCommand.Execute(
                _avalonDockWorkspaceViewModel.ActiveThemeViewModel.SelectedThemeComponent);
        }

        public async Task<bool> CloseAllDocumentsAsync()
        {
            var openDocuments = _avalonDockWorkspaceViewModel.OpenDocuments.OfType<IEditableDocument>().ToList();
            
            foreach (var document in openDocuments)
            {
                var docCount = _avalonDockWorkspaceViewModel.OpenDocuments.OfType<IEditableDocument>().Count();
                await document.Close();
                if (_avalonDockWorkspaceViewModel.OpenDocuments.OfType<IEditableDocument>().Count() == docCount)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
