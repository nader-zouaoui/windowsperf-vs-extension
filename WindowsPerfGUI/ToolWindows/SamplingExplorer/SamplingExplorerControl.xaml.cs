﻿// BSD 3-Clause License
//
// Copyright (c) 2024, Arm Limited
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
// 1. Redistributions of source code must retain the above copyright notice, this
//    list of conditions and the following disclaimer.
//
// 2. Redistributions in binary form must reproduce the above copyright notice,
//    this list of conditions and the following disclaimer in the documentation
//    and/or other materials provided with the distribution.
//
// 3. Neither the name of the copyright holder nor the names of its
//    contributors may be used to endorse or promote products derived from
//    this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.Win32;
using WindowsPerfGUI.Components;
using WindowsPerfGUI.Components.TreeListView;
using WindowsPerfGUI.Options;
using WindowsPerfGUI.Resources.Locals;
using WindowsPerfGUI.SDK;
using WindowsPerfGUI.SDK.WperfOutputs;
using WindowsPerfGUI.ToolWindows.SamplingExplorer.LineHighlighting;
using WindowsPerfGUI.ToolWindows.SamplingExplorer.SyntaxHighlighting;
using WindowsPerfGUI.ToolWindows.SamplingSetting;
using WindowsPerfGUI.Utils;

namespace WindowsPerfGUI.ToolWindows.SamplingExplorer
{
    public partial class SamplingExplorerControl : UserControl
    {
        /// <summary>
        /// Settings for the the number of available colors in the highlighter.
        /// </summary>
        private int _colorResolution;
        const int AVERAGE_LINE_HEIGHT = 30;

        readonly WperfClientFactory wperfClient = new();
        static FormattedSamplingResults formattedSamplingResults = new FormattedSamplingResults();

        private void HandleSamplingFinished(
            object sender,
            (WperfSampling serializedOutput, string stdError) e
        )
        {
            HandleSamplingStateReset(sender, e.stdError);
            try
            {
                HandleSamplingError(sender, e.serializedOutput, e.stdError);

                formattedSamplingResults.FormatSamplingResults(
                    e.serializedOutput,
                    $"{SamplingExplorerLanguagePack.ExecutedAt} {DateTime.Now.ToShortTimeString()}"
                );
                _tree.Model ??= formattedSamplingResults;
                _tree.UpdateTreeList();
            }
            catch (Exception)
            {
                return;
            }
        }

        private void HandleSPESamplingFinished(
            object sender,
            (WperfSPE serializedOutput, string stdError) e
        )
        {
            HandleSamplingStateReset(sender, e.stdError);
            try
            {
                HandleSamplingError(sender, e.serializedOutput, e.stdError);

                formattedSamplingResults.FormatSamplingResults(
                    e.serializedOutput.Sampling,
                    $"({WperfDefaults.SPEFeatureName}) - {SamplingExplorerLanguagePack.ExecutedAt} {DateTime.Now.ToShortTimeString()}"
                );
                _tree.Model ??= formattedSamplingResults;
                _tree.UpdateTreeList();
            }
            catch (Exception)
            {
                return;
            }
        }

        private void HandleSamplingStateReset(object sender, string stdError)
        {
            SamplingSettingsMonikerButton.IsEnabled = true;
            StartSamplingMonikerButton.IsEnabled = true;
            RunAndStartSamplingMonikerButton.IsEnabled = true;
            StopSamplingMonikerButton.IsEnabled = false;
        }

        private void HandleSamplingError(object sender, object serializedOutput, string stdError)
        {
            if (stdError.Contains("other WindowsPerf process acquired the wperf-driver."))
            {
                var messageBoxResult = VS.MessageBox.Show(
                    "Other WindowsPerf process acquired the wperf-driver.",
                    "Click 'Retry' to use --force-lock to force driver to give lock to current `wperf` process.",
                    icon: OLEMSGICON.OLEMSGICON_CRITICAL,
                    buttons: OLEMSGBUTTON.OLEMSGBUTTON_RETRYCANCEL
                );
                if (messageBoxResult == Microsoft.VisualStudio.VSConstants.MessageBoxResult.IDRETRY)
                {
                    SamplingSettings.samplingSettingsFrom.ForceLock = true;
                    StartSamplingMonikerButton_Click(sender, new RoutedEventArgs());
                }
                throw new Exception();
            }
            if (!string.IsNullOrEmpty(stdError))
            {
                VS.MessageBox.ShowError(ErrorLanguagePack.ErrorWindowsPerfCLI, stdError);
                throw new Exception();
            }
            if (serializedOutput == null)
            {
                VS.MessageBox.ShowError(ErrorLanguagePack.NoWindowsPerfCliData, stdError);
                throw new Exception();
            }
        }

        public SamplingExplorerControl()
        {
            InitializeComponent();

            _tree.Model = formattedSamplingResults;
            wperfClient.OnSamplingFinished += HandleSamplingFinished;
            wperfClient.OnSPESamplingFinished += HandleSPESamplingFinished;
            StopSamplingMonikerButton.IsEnabled = false;
            this._colorResolution = SamplingManager.Instance.HighlighterColorResolution;

            _tree.Loaded += (sender, e) =>
            {
                if (SamplingManager.Instance.EnableAnnotatedSymbolsSyntaxHighlighting)
                {
                    var scrollViewer = VisualTreeHelpers.GetScrollViewer(_tree);
                    scrollViewer.ScrollChanged += HighlightTreeRows;
                }
            };
            SamplingManager.Saved += (e) =>
            {
                HighlightTreeRows(null, null);
                var scrollViewer = VisualTreeHelpers.GetScrollViewer(_tree);
                if (e.EnableAnnotatedSymbolsSyntaxHighlighting)
                {
                    scrollViewer.ScrollChanged += HighlightTreeRows;
                }
                else
                {
                    scrollViewer.ScrollChanged -= HighlightTreeRows;
                }

                if (_tree.SelectedItem == null)
                    return;
                SamplingSection selecteditem =
                    (_tree.SelectedItem as TreeNode).Tag as SamplingSection;

                switch (selecteditem.SectionType)
                {
                    case SamplingSection.SamplingSectionType.SAMPLE_FUNCTION:
                        SummaryStack.Children.Clear();
                        CreateFunctionSummary(SummaryStack.Children, selecteditem);
                        break;

                    case SamplingSection.SamplingSectionType.SAMPLE_SOURCE_CODE:
                        SummaryStack.Children.Clear();
                        CreateSourceCodeSummary(SummaryStack.Children, selecteditem);
                        break;
                }
            };
        }

        private void SettingsMonikerButton_Click(object sender, RoutedEventArgs e)
        {
            if (!WPerfOptions.Instance.IsWperfInitialized)
            {
                VS.MessageBox.ShowError(
                    ErrorLanguagePack.NotInititiatedWperfErrorLine1,
                    ErrorLanguagePack.NotInititiatedWperfErrorLine2
                );
                return;
            }
            SolutionProjectOutput.GetProjectOutputAsync().FireAndForget();
            string dialogWindowTitle = SamplingSettingsLanguagePack.WindowTitle;
            if (
                WPerfOptions.Instance.WperfCurrentVersion.Components[0].ComponentVersion
                != WperfDefaults.WPERF_MIN_VERSION
            )
            {
                if (WPerfOptions.Instance.WperfVersionCheckIgnore != true)
                {
                    VS.MessageBox.ShowError(
                        string.Format(
                            ErrorLanguagePack.MinimumVersionMismatch,
                            WperfDefaults.WPERF_MIN_VERSION
                        ),
                        ErrorLanguagePack.MinimumVersionMismatchLine2
                    );
                    return;
                }
                var messageBoxResult = VS.MessageBox.ShowWarning(
                    string.Format(
                        ErrorLanguagePack.MinimumVersionMismatch,
                        WperfDefaults.WPERF_MIN_VERSION
                    )
                );
                if (
                    messageBoxResult == Microsoft.VisualStudio.VSConstants.MessageBoxResult.IDCANCEL
                )
                {
                    return;
                }
                dialogWindowTitle += $" - (UNSTABLE)";
            }

            SamplingSettingDialog samplingSettingsDialog = new();
            samplingSettingsDialog.Title = dialogWindowTitle;
            samplingSettingsDialog.ShowDialog();
        }

        private void StartSamplingMonikerButton_Click(object sender, RoutedEventArgs e)
        {
            if (!WPerfOptions.Instance.IsWperfInitialized)
                return;
            if (!SamplingSettings.AreSettingsFilled)
            {
                VS.MessageBox.ShowError(ErrorLanguagePack.IncompleteSamplingSettingsLine1);
                return;
            }

            if (SamplingSettings.IsSampling)
            {
                VS.MessageBox.ShowError(
                    ErrorLanguagePack.RunningSamplingOverlapLine1,
                    ErrorLanguagePack.RunningSamplingOverlapLine2
                );
                return;
            }

            _ = wperfClient
                .StartSamplingAsync()
                .ContinueWith(
                    async (t) =>
                    {
                        while (!t.IsCompleted)
                        {
                            Thread.Sleep(1000);
                        }
                        if (t.IsFaulted)
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                            await VS.MessageBox.ShowErrorAsync(ErrorLanguagePack.WperfPathChanged);
                            Trace.WriteLine(t.Exception.Message);
                            wperfClient.Reinitialize();

                            SamplingSettingsMonikerButton.IsEnabled = true;
                            StartSamplingMonikerButton.IsEnabled = true;
                            RunAndStartSamplingMonikerButton.IsEnabled = true;
                            StopSamplingMonikerButton.IsEnabled = false;
                        }
                    },
                    CancellationToken.None,
                    (TaskContinuationOptions)TaskCreationOptions.None,
                    TaskScheduler.Default
                );

            SamplingSettingsMonikerButton.IsEnabled = false;
            StartSamplingMonikerButton.IsEnabled = false;
            RunAndStartSamplingMonikerButton.IsEnabled = false;
            StopSamplingMonikerButton.IsEnabled = true;
        }

        private void StopSamplingMonikerButton_Click(object sender, RoutedEventArgs e)
        {
            if (SamplingSettings.samplingSettingsFrom.IsSPEEnabled)
            {
                wperfClient.StopSPESampling();
            }
            else
            {
                wperfClient.StopSampling();
            }
            SamplingSettingsMonikerButton.IsEnabled = true;
            StartSamplingMonikerButton.IsEnabled = true;
            RunAndStartSamplingMonikerButton.IsEnabled = true;
            StopSamplingMonikerButton.IsEnabled = false;
        }

        private void HighlightEditor(
            SamplingSection samplingSection,
            bool useAbsoluteOverhead = false
        )
        {
            if (
                samplingSection.SectionType
                != SamplingSection.SamplingSectionType.SAMPLE_SOURCE_CODE
            )
            {
                foreach (SamplingSection child in samplingSection.Children)
                {
                    HighlightEditor(child, useAbsoluteOverhead);
                }
            }

            if (!samplingSection.IsFileExists)
            {
                return;
            }

            HighlighterDict.AddFileToHighlight(samplingSection, useAbsoluteOverhead);
        }

        private void _tree_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e
        )
        {
            TreeList treeList = sender as TreeList;
            SummaryStack.Children.Clear();
            if (treeList == null || treeList.SelectedItem == null)
                return;
            SamplingSection selecteditem =
                (treeList.SelectedItem as TreeNode).Tag as SamplingSection;
            switch (selecteditem.SectionType)
            {
                case SamplingSection.SamplingSectionType.ROOT:
                    CreateRootSummary(SummaryStack.Children, selecteditem);
                    HighlighterDict.Clear();
                    HighlightEditor(selecteditem, useAbsoluteOverhead: true);
                    break;
                case SamplingSection.SamplingSectionType.SAMPLE_EVENT:
                    CreateEventSummary(SummaryStack.Children, selecteditem);
                    HighlighterDict.Clear();
                    HighlightEditor(selecteditem);
                    break;
                case SamplingSection.SamplingSectionType.SAMPLE_FUNCTION:
                    CreateFunctionSummary(SummaryStack.Children, selecteditem);
                    break;

                case SamplingSection.SamplingSectionType.SAMPLE_SOURCE_CODE:
                    CreateSourceCodeSummary(SummaryStack.Children, selecteditem);
                    break;
                default:
                    break;
            }
        }

        private void CreateSourceCodeSummary(
            UIElementCollection children,
            SamplingSection samplingSection
        )
        {
            if (
                samplingSection.SectionType
                != SamplingSection.SamplingSectionType.SAMPLE_SOURCE_CODE
            )
            {
                return;
            }

            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.FilePath}: ")),
                        new Run(samplingSection.Name)
                    },
                    _localFontSizes.lg,
                    _localFontWeights.bold
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.LineNumber}: ")),
                        new Run(samplingSection.LineNumber.ToString())
                    },
                    _localFontSizes.md
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.Hits}: ")),
                        new Run($"{samplingSection.Hits}")
                    },
                    _localFontSizes.md
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.Overhead}: ")),
                        new Run($"{samplingSection.OverheadPercentage}")
                    },
                    _localFontSizes.md
                )
            );
            StackPanel asseblyPanel = GenerateAssemblyStackPanel(samplingSection);
            if (asseblyPanel != null)
                children.Add(asseblyPanel);
        }

        private StackPanel GenerateAssemblyStackPanel(SamplingSection samplingSection)
        {
            if (samplingSection.Assemblies == null || samplingSection.Assemblies.Count == 0)
            {
                return null;
            }

            StackPanel asseblyPanel = new StackPanel();

            Grid asseblyTitleGrid = GenerateAssemblyLineGrid(
                "Address",
                "Instruction",
                isTitle: true
            );

            asseblyPanel.Children.Add(asseblyTitleGrid);

            Brush highlightColor = ColorGenerator.GenerateColor(
                samplingSection.Overhead ?? 0,
                this._colorResolution
            );
            int offset = 1;
            bool stopOffsetCount = false;
            foreach (ExtendedAssembly assemblyLine in samplingSection.Assemblies)
            {
                Grid asseblyLineGrid = GenerateAssemblyLineGrid(
                    firstColumn: assemblyLine.Address,
                    secondColumn: assemblyLine.Instruction,
                    textColor: assemblyLine.IsHighlighted ? highlightColor : null
                );

                asseblyPanel.Children.Add(asseblyLineGrid);

                if (assemblyLine.IsHighlighted)
                    stopOffsetCount = true;

                if (!stopOffsetCount)
                    offset++;
            }

            int numberOfVisibleLines = (int)AssemblyScrollViewer.ActualHeight / AVERAGE_LINE_HEIGHT;
            int highlightedTextPosition = (offset + 4) * AVERAGE_LINE_HEIGHT;
            if ((offset + 5) > numberOfVisibleLines)
            {
                int centeredPositionForHighlightedText =
                    highlightedTextPosition - (numberOfVisibleLines * AVERAGE_LINE_HEIGHT / 2);
                AssemblyScrollViewer.ScrollToVerticalOffset(centeredPositionForHighlightedText);
            }
            else
            {
                AssemblyScrollViewer.ScrollToVerticalOffset(0);
            }

            return asseblyPanel;
        }

        private Grid GenerateAssemblyLineGrid(
            string firstColumn,
            string secondColumn,
            bool isTitle = false,
            Brush textColor = null
        )
        {
            Grid asseblyTitleGrid = new();
            asseblyTitleGrid.ColumnDefinitions.Add(
                new ColumnDefinition() { Width = new GridLength(100, GridUnitType.Pixel) }
            );
            asseblyTitleGrid.ColumnDefinitions.Add(
                new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }
            );

            Brush addressColor =
                textColor
                ?? (
                    (
                        SamplingManager.Instance.EnableDisassemblySyntaxHighlighting == true
                        && isTitle == false
                    )
                        ? DrawingcolorToSolidBrush(SamplingManager.Instance.AddressColor)
                        : null
                );

            TextBlock addressTitleTB = GenerateTextBlock(
                InlineText: [isTitle ? new Bold(new Run(firstColumn)) : new Run(firstColumn)],
                fontSize: isTitle ? _localFontSizes.lg : _localFontSizes.md,
                textColor: addressColor
            );
            FontFamily _fontFamily = new("Consolas");
            addressTitleTB.FontFamily = _fontFamily;
            addressTitleTB.SetValue(Grid.ColumnProperty, 0);

            asseblyTitleGrid.Children.Add(addressTitleTB);

            if (isTitle)
            {
                TextBlock instructionTitleTB = GenerateTextBlock(
                    InlineText: [new Bold(new Run(secondColumn))],
                    fontSize: _localFontSizes.lg,
                    textColor: textColor
                );
                instructionTitleTB.SetValue(Grid.ColumnProperty, 1);
                instructionTitleTB.FontFamily = _fontFamily;

                asseblyTitleGrid.Children.Add(instructionTitleTB);
            }
            else
            {
                Grid instructionGrid = new();
                instructionGrid.ColumnDefinitions.Add(
                    new ColumnDefinition() { Width = new GridLength(100, GridUnitType.Pixel) }
                );
                instructionGrid.ColumnDefinitions.Add(
                    new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }
                );
                instructionGrid.SetValue(Grid.ColumnProperty, 1);

                List<string> instructionList = [.. secondColumn.Split(null, 2)];

                string instruction = instructionList[0];

                TextBlock instructionTextBlock = GenerateTextBlock(
                    InlineText: [new Bold(new Run(instruction))],
                    fontSize: _localFontSizes.md,
                    textColor: textColor
                        ?? (
                            SamplingManager.Instance.EnableDisassemblySyntaxHighlighting == true
                                ? DrawingcolorToSolidBrush(SamplingManager.Instance.MnemonicColor)
                                : null
                        )
                );
                instructionTextBlock.FontFamily = _fontFamily;
                instructionTextBlock.SetValue(Grid.ColumnProperty, 0);
                instructionGrid.Children.Add(instructionTextBlock);
                if (instructionList.Count > 1)
                {
                    string arguments = instructionList[1].Trim();

                    List<Inline> highlightedArgsInlineList = [new Bold(new Run(arguments))];

                    if (
                        SamplingManager.Instance.EnableDisassemblySyntaxHighlighting
                        && textColor == null
                    )
                    {
                        highlightedArgsInlineList = SyntaxHighlighter.HighlightCode(
                            arguments,
                            Rules.GetARM64AssemblyRules(),
                            defaultColor: SamplingManager.Instance.RegisterColor
                        );
                    }
                    TextBlock argumentsTextBlock = GenerateTextBlock(
                        InlineText: highlightedArgsInlineList,
                        fontSize: _localFontSizes.md,
                        textColor: textColor,
                        fontWeight: _localFontWeights.bold
                    );
                    argumentsTextBlock.FontFamily = _fontFamily;
                    argumentsTextBlock.SetValue(Grid.ColumnProperty, 1);
                    instructionGrid.Children.Add(argumentsTextBlock);
                }
                asseblyTitleGrid.Children.Add(instructionGrid);
            }
            return asseblyTitleGrid;
        }

        private static SolidColorBrush DrawingcolorToSolidBrush(System.Drawing.Color color)
        {
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }

        private void CreateFunctionSummary(
            UIElementCollection children,
            SamplingSection samplingSection
        )
        {
            if (samplingSection.SectionType != SamplingSection.SamplingSectionType.SAMPLE_FUNCTION)
            {
                return;
            }
            var functionNameInlines = new List<Inline>()
            {
                new Bold(new Run($"{SamplingExplorerLanguagePack.FunctionName}: ")),
            };

            if (SamplingManager.Instance.EnableAnnotatedSymbolsSyntaxHighlighting)
            {
                functionNameInlines.AddRange(
                    SyntaxHighlighter.HighlightCode(
                        samplingSection.Name,
                        Rules.GetCPPRules(),
                        defaultColor: System.Drawing.Color.White,
                        configureHighlightedRun: block => block.FontWeight = FontWeights.Bold
                    )
                );
            }
            else
            {
                functionNameInlines.Add(new Run(samplingSection.Name));
            }
            children.Add(
                GenerateTextBlock(functionNameInlines, _localFontSizes.lg, _localFontWeights.bold)
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.Interval}: ")),
                        new Run(samplingSection.Parent.Frequency)
                    },
                    _localFontSizes.md
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.Hits}: ")),
                        new Run($"{samplingSection.Hits}")
                    },
                    _localFontSizes.md
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.Overhead}: ")),
                        new Run($"{samplingSection.OverheadPercentage}")
                    },
                    _localFontSizes.md
                )
            );
        }

        private void CreateEventSummary(
            UIElementCollection children,
            SamplingSection samplingSection
        )
        {
            if (samplingSection.SectionType != SamplingSection.SamplingSectionType.SAMPLE_EVENT)
            {
                return;
            }

            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.EventName}: ")),
                        new Run(samplingSection.Name)
                    },
                    _localFontSizes.lg,
                    _localFontWeights.bold
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.Interval}: ")),
                        new Run(samplingSection.Frequency)
                    },
                    _localFontSizes.md
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(
                            new Run($"{SamplingExplorerLanguagePack.NumberOfCollectedSamples}: ")
                        ),
                        new Run($"{samplingSection.Hits}")
                    },
                    _localFontSizes.md
                )
            );
        }

        private void CreateRootSummary(
            UIElementCollection children,
            SamplingSection samplingSection
        )
        {
            if (samplingSection.SectionType != SamplingSection.SamplingSectionType.ROOT)
            {
                return;
            }

            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.Name}: ")),
                        new Run(samplingSection.Name)
                    },
                    _localFontSizes.lg,
                    _localFontWeights.bold
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.PeFile}: ")),
                        new Run(samplingSection.PeFile)
                    },
                    _localFontSizes.md
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.PdbFile}: ")),
                        new Run(samplingSection.PdbFile)
                    },
                    _localFontSizes.md
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.SamplesGenerated}: ")),
                        new Run($"{samplingSection.Hits}")
                    },
                    _localFontSizes.md
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>()
                    {
                        new Bold(new Run($"{SamplingExplorerLanguagePack.SamplesDropped}: ")),
                        new Run($"{samplingSection.SamplesDropped}")
                    },
                    _localFontSizes.md
                )
            );
            children.Add(
                GenerateTextBlock(
                    new List<Inline>() { new Run($"{SamplingExplorerLanguagePack.Modules}: ") },
                    _localFontSizes.md,
                    _localFontWeights.bold
                )
            );

            foreach (var module in samplingSection.Modules)
            {
                children.Add(
                    GenerateTextBlock(
                        new List<Inline>()
                        {
                            new Bold(new Run($"{SamplingExplorerLanguagePack.Name}: ")),
                            new Run(module.Name)
                        },
                        layer: 1
                    )
                );
                children.Add(
                    GenerateTextBlock(
                        new List<Inline>()
                        {
                            new Bold(new Run($"{SamplingExplorerLanguagePack.FilePath}: ")),
                            new Run(module.Path)
                        },
                        layer: 2
                    )
                );
            }

            children.Add(
                GenerateTextBlock(
                    new List<Inline>() { new Run($"{SamplingExplorerLanguagePack.EventList}: ") },
                    _localFontSizes.md,
                    _localFontWeights.bold
                )
            );

            foreach (var sampledEvent in samplingSection.Children)
            {
                children.Add(
                    GenerateTextBlock(
                        new List<Inline>()
                        {
                            new Bold(new Run($"{SamplingExplorerLanguagePack.Name}: ")),
                            new Run(sampledEvent.Name)
                        },
                        layer: 1
                    )
                );
                children.Add(
                    GenerateTextBlock(
                        new List<Inline>()
                        {
                            new Bold(new Run($"{SamplingSettingsLanguagePack.Frequency} ")),
                            new Run(sampledEvent.Frequency)
                        },
                        layer: 2
                    )
                );
                var saveAsButton = new CustomButtonControl()
                {
                    Content = SamplingSettingsLanguagePack.Save,
                    Margin = new Thickness { Top = 10 },
                    Padding = new Thickness
                    {
                        Top = 5,
                        Bottom = 5,
                        Left = 20,
                        Right = 20
                    },
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                saveAsButton.Click += (sender, e) => SaveAs_Click(samplingSection);
                children.Add(saveAsButton);
            }
        }

        private void SaveAs_Click(SamplingSection samplingSection)
        {
            if (string.IsNullOrEmpty(samplingSection.StringifiedJsonOutput))
                return;

            SaveFileDialog openfileDialog = new SaveFileDialog() { DefaultExt = "json" };
            if (openfileDialog.ShowDialog() != true)
                return;

            File.WriteAllText(openfileDialog.FileName, samplingSection.StringifiedJsonOutput);
        }

        private enum _localFontSizes
        {
            sm = 12,
            md = 14,
            lg = 16,
        }

        private enum _localFontWeights
        {
            normal,
            bold,
        }

        private TextBlock GenerateTextBlock(
            List<Inline> InlineText,
            _localFontSizes fontSize = _localFontSizes.sm,
            _localFontWeights fontWeight = _localFontWeights.normal,
            int layer = 0,
            Brush textColor = null
        )
        {
            FontWeight _fontWeight =
                fontWeight == _localFontWeights.normal ? FontWeights.Normal : FontWeights.Bold;
            double marginTop =
                fontSize == _localFontSizes.sm
                    ? 5
                    : fontSize == _localFontSizes.md
                        ? 10
                        : 15;
            Thickness _margin = new Thickness(layer * 15, marginTop, 0, 0);
            TextBlock textBlock = new TextBlock()
            {
                FontSize = (double)fontSize,
                FontWeight = _fontWeight,
                Margin = _margin
            };
            textBlock.Inlines.AddRange(InlineText);
            if (textColor != null)
            {
                textBlock.Foreground = textColor;
            }

            return textBlock;
        }

        private void _tree_MouseDoubleClick(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e
        )
        {
            var treeList = sender as TreeList;
            if (treeList.SelectedItem == null)
                return;
            SamplingSection selecteditem =
                (treeList.SelectedItem as TreeNode).Tag as SamplingSection;
            if (!selecteditem.IsFileExists)
                return;
            string filePath = selecteditem.Name;
            OpenVSDocument(filePath, (int)selecteditem.LineNumber);
        }

        private void OpenVSDocument(string filePath, int lineNumber)
        {
            _ = Task.Run(async () =>
            {
                DocumentView docView;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                docView = await VS.Documents.OpenAsync(filePath);

                SnapshotPoint position = docView.TextView.Caret.Position.BufferPosition;
                docView.TextView.Caret.MoveTo(
                    position.Snapshot.GetLineFromLineNumber(lineNumber - 1).End
                );
                docView.TextView.ViewScroller.EnsureSpanVisible(
                    new SnapshotSpan(
                        position.Snapshot.GetLineFromLineNumber(lineNumber - 1).Start,
                        position.Snapshot.GetLineFromLineNumber(lineNumber - 1).End
                    )
                );
            });
        }

        private void ClearListMonikerButton_Click(object sender, RoutedEventArgs e)
        {
            formattedSamplingResults.ClearSampling();
            _tree.UpdateTreeList();
        }

        private void TextBlock_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var textBlock = sender as TextBlock;
            string filePath = textBlock.Text;
            SamplingSection selecteditem = textBlock.DataContext as SamplingSection;
            bool isFileExists = File.Exists(filePath);
            if (!isFileExists)
                return;
            int lineNumber = selecteditem?.LineNumber != null ? (int)selecteditem?.LineNumber : 0;
            OpenVSDocument(filePath, lineNumber);
        }

#pragma warning disable VSTHRD100 // Avoid async void methods
        private async void RunAndStartSamplingMonikerButton_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            Project project = await VS.Solutions.GetActiveProjectAsync();
            RunAndStartSamplingMonikerButton.IsEnabled = false;
            StartSamplingMonikerButton.IsEnabled = false;
            bool buildSucceeded = await project.BuildAsync(BuildAction.Rebuild);
            RunAndStartSamplingMonikerButton.IsEnabled = true;
            StartSamplingMonikerButton.IsEnabled = true;

            if (!buildSucceeded)
                return;

            StartSamplingMonikerButton_Click(sender, e);
        }

        private void LoadJSONMonikerButton_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new OpenFileDialog();
            fileDialog.DefaultExt = "json";
            fileDialog.Filter = "JSON files (*.json)|*.json";

            bool? result = fileDialog.ShowDialog();
            if (result != true)
                return;

            string filename = fileDialog.FileName;
            using StreamReader reader = new(filename);

            string json = reader.ReadToEnd();
            bool isSPESampling = JsonTypeChecker.IsJsonType<WperfSPE>(json);

            WperfSampling samplingSection = isSPESampling
                ? WperfSPE.FromJson(json).Sampling
                : WperfSampling.FromJson(json);

            try
            {
                formattedSamplingResults.FormatSamplingResults(
                    samplingSection,
                    $"Read from file: {filename}"
                );

                _tree.Model ??= formattedSamplingResults;
                _tree.UpdateTreeList();
            }
            catch (Exception err)
            {
                VS.MessageBox.ShowError("Error loading JSON file", err.Message);
            }
        }

        private string GetTextFromTextBlockInlines(TextBlock tb)
        {
            return string.Join(
                null,
                tb.Inlines.Select(el => new TextRange(el.ContentStart, el.ContentEnd).Text)
            );
        }

        private void HighlightTreeRows(object sender, object e)
        {
            foreach (TreeNode levelThreeNode in _tree.Rows.Where(node => node.Level == 2))
            {
                if (
                    _tree.ItemContainerGenerator.ContainerFromItem(levelThreeNode)
                    is not TreeListItem treeListItem
                )
                    continue;

                TextBlock textBlock = VisualTreeHelpers.FindTextBlock(treeListItem);
                if (textBlock == null)
                    continue;
                TextBlock newTextBlock = new();

                string text = GetTextFromTextBlockInlines(textBlock);
                if (!SamplingManager.Instance.EnableAnnotatedSymbolsSyntaxHighlighting)
                {
                    newTextBlock.Text = text;
                }
                else
                {
                    newTextBlock.Inlines.AddRange(
                        SyntaxHighlighting.SyntaxHighlighter.HighlightCode(
                            text,
                            SyntaxHighlighting.Rules.GetCPPRules(),
                            defaultColor: System.Drawing.Color.White,
                            configureHighlightedRun: block => block.FontWeight = FontWeights.Bold
                        )
                    );
                }

                if (VisualTreeHelper.GetParent(textBlock) is not StackPanel stackPanel)
                    continue;
                stackPanel.Children.Remove(textBlock);
                stackPanel.Children.Add(newTextBlock);
            }
            VisualTreeHelpers.UpdateUI();
        }
    }
}
