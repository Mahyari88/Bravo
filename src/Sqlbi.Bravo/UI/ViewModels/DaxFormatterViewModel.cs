﻿using Microsoft.Extensions.Logging;
using Sqlbi.Bravo.Client.DaxFormatter;
using Sqlbi.Bravo.Core.Helpers;
using Sqlbi.Bravo.Core.Logging;
using Sqlbi.Bravo.Core.Services.Interfaces;
using Sqlbi.Bravo.Core.Settings.Interfaces;
using Sqlbi.Bravo.UI.DataModel;
using Sqlbi.Bravo.UI.Framework.Commands;
using Sqlbi.Bravo.UI.Framework.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace Sqlbi.Bravo.UI.ViewModels
{
    internal class DaxFormatterViewModel : BaseViewModel
    {
        internal const int SubViewIndex_Loading = 0;
        internal const int SubViewIndex_Start = 1;
        internal const int SubViewIndex_ChooseFormulas = 2;
        internal const int SubViewIndex_Progress = 3;
        internal const int SubViewIndex_Changes = 4;
        internal const int SubViewIndex_Finished = 5;

        private readonly IDaxFormatterService _formatter;
        private readonly IGlobalSettingsProviderService _settings;
        private readonly ILogger _logger;
        private readonly DispatcherTimer _timer = new();
        private bool _initialized = false;

        public DaxFormatterViewModel(IDaxFormatterService formatter, IGlobalSettingsProviderService settings, ILogger<DaxFormatterViewModel> logger)
        {
            _formatter = formatter;
            _settings = settings;
            _logger = logger;

            _logger.Trace();

            ViewIndex = SubViewIndex_Loading;
            PreviewChanges = true;

            FormatAnalyzeCommand = new RelayCommand(async () => await AnalyzeAsync());
            FormatMakeChangesCommand = new RelayCommand(async () => await MakeChangesAsync());
            HelpCommand = new RelayCommand(() => ShowHelp());
            RefreshCommand = new RelayCommand(async () => await RefreshAsync());
            ChangeFormulasCommand = new RelayCommand(() => ChooseFormulas());
            ApplySelectedFormulaChangesCommand = new RelayCommand(() => SelectedFormulasChanged());
            OpenLogCommand = new RelayCommand(() => OpenLog());
            CancelFormattingCommand = new RelayCommand(() => CancelFormatting());

            _timer = new DispatcherTimer
            {
                Interval = new TimeSpan(0, 0, 15)
            };
            _timer.Tick += new EventHandler((s, e) => OnPropertyChanged(nameof(TimeSinceLastSync)));
            _timer.Start();
        }

        public TabItemViewModel ParentTab { get; set; }

        public int ViewIndex { get; set; }

        public bool PreviewChanges { get; set; }

        public ICommand OpenLogCommand { get; set; }

        public ICommand HelpCommand { get; set; }

        public ICommand RefreshCommand { get; set; }

        public ICommand ChangeFormulasCommand { get; set; }

        public ICommand ApplySelectedFormulaChangesCommand { get; set; }

        public ICommand CancelFormattingCommand { get; set; }

        public bool TermsAccepted { get; set; }

        public bool CanFormat => TermsAccepted && SelectionTreeData.SelectedTreeItemCount > 0;

        public ICommand FormatAnalyzeCommand { get; set; }

        public ICommand FormatMakeChangesCommand { get; set; }

        public bool FormatCommandIsRunning { get; set; }

        public bool FormatCommandIsEnabled { get; set; }

        public MeasureSelectionViewModel SelectionTreeData { get; set; }

        public string ProgressDetails { get; set; }

        public MeasureInfoViewModel AllFormulasSelected { get; set; }

        public MeasureInfoViewModel NeedFormattingSelected { get; set; }

        public string LoadingDetails { get; set; }

        public DateTime LastSyncTime { get; private set; }

        public string TimeSinceLastSync => LastSyncTime.HumanizeElapsed();

        public int MeasuresFormatted { get; set; }

        public int AnalyzedMeasureCount { get; set; }

        public ObservableCollection<MeasureInfoViewModel> Measures { get; set; } = new();

        public ObservableCollection<MeasureInfoViewModel> MeasuresNeedingFormatting => new(Measures.Where((m) => !m.IsAlreadyFormatted).ToList());

        public bool NoMeasuresNeedingFormatting => MeasuresNeedingFormatting.Count == 0;

        private async Task InitializeOrRefreshFormatter()
        {
            _logger.Trace();

            FormatCommandIsEnabled = false;
            LoadingDetails = "Connecting to data";
            await _formatter.InitilizeOrRefreshAsync(ParentTab.ConnectionSettings);

            LastSyncTime = DateTime.UtcNow;
            OnPropertyChanged(nameof(TimeSinceLastSync));

            FormatCommandIsEnabled = true;
            LoadMeasuresForSelection();

            _initialized = true;
        }

        private void LoadMeasuresForSelection()
        {
            _logger.Trace();

            var msvm = new MeasureSelectionViewModel();

            foreach (var measure in _formatter.Measures)
            {
                var addedMeasure = false;

                foreach (var table in msvm.Tables)
                {
                    if (table.Name == measure.TableName)
                    {
                        table.Measures.Add(new TreeItem(msvm, table) { Name = measure.Name, Formula = measure.Expression, TabularObject = measure });
                        addedMeasure = true;
                        break;
                    }
                }

                if (!addedMeasure)
                {
                    var newTable = new TreeItem(msvm) { Name = measure.TableName };
                    newTable.Measures.Add(new TreeItem(msvm, newTable) { Name = measure.Name, Formula = measure.Expression, TabularObject = measure });
                    msvm.Tables.Add(newTable);
                }
            }

            SelectionTreeData = msvm;
        }

        internal void EnsureInitialized()
        {
            _logger.Trace();

            if (!_initialized)
            {
                Task.Run(async () => await RefreshAsync());
            }
        }

        private async Task RefreshAsync()
        {
            _logger.Trace();

            try
            {
                ViewIndex = SubViewIndex_Loading;

                await InitializeOrRefreshFormatter();
                
                ViewIndex = SubViewIndex_Start;
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.DaxFormatterException, ex);

                ParentTab.DisplayError($"Unable to connect{Environment.NewLine}{ex.Message}", InitializeOrRefreshFormatter);
            }
        }

        private void ChooseFormulas()
        {
            _logger.Trace();

            ViewIndex = SubViewIndex_ChooseFormulas;
        }

        private void SelectedFormulasChanged()
        {
            _logger.Information(LogEvents.DaxFormatterViewAction, "{@Details}", new object[] { new
            {
                Action = "AnalyzeFormatSelectionChanged"
            }});

            OnPropertyChanged(nameof(CanFormat));

            ViewIndex = SubViewIndex_Start;
        }

        private void OpenLog()
        {
            _logger.Trace();

            // TODO REQUIREMENTS: Open log file
        }

        private void CancelFormatting()
        {
            ViewIndex = SubViewIndex_Start;
        }

        private void ShowHelp()
        {
            _logger.Trace();

            Views.ShellView.Instance.ShowMediaDialog(new HowToFormatCodeHelp());
        }

        private async Task AnalyzeAsync()
        {
            _logger.Information(LogEvents.DaxFormatterViewAction, "{@Details}", new object[] { new
            {
                Action = "AnalyzeFormat",
                Preview = PreviewChanges
            }});

            ProgressDetails = "Identifying formulas to format";
            ViewIndex = SubViewIndex_Progress;

            var tabularObjects = SelectionTreeData.Tables.SelectMany(t => t.Measures.Where(m => !string.IsNullOrWhiteSpace(m.Formula) && (m.IsSelected ?? false))).Select((i) => i.TabularObject).ToList();
            var formattedTabularObjects = await _formatter.FormatAsync(tabularObjects, _settings.Application);

            Measures.Clear();

            foreach (var formattedTabularObject in formattedTabularObjects)
            {
                if (formattedTabularObject is TabularMeasure measure)
                {
                    Measures.Add(new MeasureInfoViewModel
                    {
                        TabularObject = measure,
                        Name = measure.Name,
                        OriginalDax = measure.Expression,
                        FormatterDax = measure.ExpressionFormatted,
                    });
                }
            }

            OnPropertyChanged(nameof(MeasuresNeedingFormatting));
            OnPropertyChanged(nameof(NoMeasuresNeedingFormatting));

            AnalyzedMeasureCount = Measures.Count;

            if (PreviewChanges)
            {
                NeedFormattingSelected = MeasuresNeedingFormatting.FirstOrDefault();
                AllFormulasSelected = Measures.First();
                ViewIndex = SubViewIndex_Changes;
            }
            else
            {
                ProgressDetails = "Applying formatting changes";
                await ApplyFormattingChangesToModelAsync();

                ViewIndex = SubViewIndex_Finished;
            }
        }

        private async Task ApplyFormattingChangesToModelAsync()
        {
            _logger.Information(LogEvents.DaxFormatterViewAction, "{@Details}", new object[] { new
            {
                Action = "ApplyFormat"
            }});

            var changedTabularObjects = Measures.Where((m) => !m.IsAlreadyFormatted && m.Reformat).Select((m) => m.TabularObject).ToList();           

            try
            {
                await _formatter.ApplyFormatAsync(changedTabularObjects);
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.DaxFormatterException, ex);

                ParentTab.DisplayError($"Unable to save changes{Environment.NewLine}{ex.Message}", ApplyFormattingChangesToModelAsync);
            }

            MeasuresFormatted = changedTabularObjects.Count;
        }

        private async Task MakeChangesAsync()
        {
            _logger.Trace();

            await ApplyFormattingChangesToModelAsync();

            ViewIndex = SubViewIndex_Finished;
        }
    }
}
