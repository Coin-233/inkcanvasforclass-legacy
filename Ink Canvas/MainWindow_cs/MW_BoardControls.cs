using Ink_Canvas.Helpers;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using System.Windows.Controls;

namespace Ink_Canvas {
    public partial class MainWindow : Window {
        private StrokeCollection[] strokeCollections = new StrokeCollection[101];
        private bool[] whiteboadLastModeIsRedo = new bool[101];
        private StrokeCollection lastTouchDownStrokeCollection = new StrokeCollection();

        private int CurrentWhiteboardIndex = 1;
        private int WhiteboardTotalCount = 1;
        private TimeMachineHistory[][] TimeMachineHistories = new TimeMachineHistory[101][]; //最多99页，0用来存储非白板时的墨迹以便还原

        private void SaveStrokes(bool isBackupMain = false) {
            if (isBackupMain) {
                var timeMachineHistory = timeMachine.ExportTimeMachineHistory();
                TimeMachineHistories[0] = timeMachineHistory;
                timeMachine.ClearStrokeHistory();
            } else {
                var timeMachineHistory = timeMachine.ExportTimeMachineHistory();
                TimeMachineHistories[CurrentWhiteboardIndex] = timeMachineHistory;
                timeMachine.ClearStrokeHistory();
            }
        }

        private void ClearStrokes(bool isErasedByCode) {
            _currentCommitType = CommitReason.ClearingCanvas;
            if (isErasedByCode) _currentCommitType = CommitReason.CodeInput;
            inkCanvas.Strokes.Clear();
            _currentCommitType = CommitReason.UserInput;
        }

        private void RestoreStrokes(bool isBackupMain = false) {
            try {
                if (TimeMachineHistories[CurrentWhiteboardIndex] == null) return; //防止白板打开后不居中
                if (isBackupMain) {
                    timeMachine.ImportTimeMachineHistory(TimeMachineHistories[0]);
                    foreach (var item in TimeMachineHistories[0]) ApplyHistoryToCanvas(item);
                } else {
                    timeMachine.ImportTimeMachineHistory(TimeMachineHistories[CurrentWhiteboardIndex]);
                    foreach (var item in TimeMachineHistories[CurrentWhiteboardIndex]) ApplyHistoryToCanvas(item);
                }
            }
            catch {
                // ignored
            }
        }

        private async void BtnWhiteBoardPageIndex_Click(object sender, EventArgs e) {
            if (sender == BtnLeftPageListWB) {
                if (BoardBorderLeftPageListView.Visibility == Visibility.Visible) {
                    AnimationsHelper.HideWithSlideAndFade(BoardBorderLeftPageListView);
                } else {
                    AnimationsHelper.HideWithSlideAndFade(BoardBorderRightPageListView);
                    RefreshBlackBoardSidePageListView();
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardBorderLeftPageListView);
                    await Task.Delay(1);
                    ScrollViewToVerticalTop(
                        (ListViewItem)BlackBoardLeftSidePageListView.ItemContainerGenerator.ContainerFromIndex(
                            CurrentWhiteboardIndex - 1), BlackBoardLeftSidePageListScrollViewer);
                }
            } else if (sender == BtnRightPageListWB)
            {
                if (BoardBorderRightPageListView.Visibility == Visibility.Visible) {
                    AnimationsHelper.HideWithSlideAndFade(BoardBorderRightPageListView);
                } else {
                    AnimationsHelper.HideWithSlideAndFade(BoardBorderLeftPageListView);
                    RefreshBlackBoardSidePageListView();
                    AnimationsHelper.ShowWithSlideFromBottomAndFade(BoardBorderRightPageListView);
                    await Task.Delay(1);
                    ScrollViewToVerticalTop(
                        (ListViewItem)BlackBoardRightSidePageListView.ItemContainerGenerator.ContainerFromIndex(
                            CurrentWhiteboardIndex - 1), BlackBoardRightSidePageListScrollViewer);
                }
            }

        }

        private void BtnWhiteBoardSwitchPrevious_Click(object sender, EventArgs e) {
            if (CurrentWhiteboardIndex <= 1) return;

            SaveStrokes();

            ClearStrokes(true);
            CurrentWhiteboardIndex--;

            RestoreStrokes();

            UpdateIndexInfoDisplay();
        }

        private void BtnWhiteBoardSwitchNext_Click(object sender, EventArgs e) {
            Trace.WriteLine("113223234");

            if (Settings.Automation.IsAutoSaveStrokesAtClear &&
                inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber) SaveScreenshot(true);
            if (CurrentWhiteboardIndex >= WhiteboardTotalCount) {
                BtnWhiteBoardAdd_Click(sender, e);
                return;
            }

            SaveStrokes();

            ClearStrokes(true);
            CurrentWhiteboardIndex++;

            RestoreStrokes();

            UpdateIndexInfoDisplay();
        }

        private void BtnWhiteBoardAdd_Click(object sender, EventArgs e) {
            if (WhiteboardTotalCount >= 99) return;
            if (Settings.Automation.IsAutoSaveStrokesAtClear &&
                inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber) SaveScreenshot(true);
            SaveStrokes();
            ClearStrokes(true);

            WhiteboardTotalCount++;
            CurrentWhiteboardIndex++;

            if (CurrentWhiteboardIndex != WhiteboardTotalCount)
                for (var i = WhiteboardTotalCount; i > CurrentWhiteboardIndex; i--)
                    TimeMachineHistories[i] = TimeMachineHistories[i - 1];

            UpdateIndexInfoDisplay();

            if (WhiteboardTotalCount >= 99) BtnWhiteBoardAdd.IsEnabled = false;

            if (BlackBoardLeftSidePageListView.Visibility == Visibility.Visible) {
                RefreshBlackBoardSidePageListView();
            }
        }

        private void BtnWhiteBoardDelete_Click(object sender, RoutedEventArgs e) {
            ClearStrokes(true);

            if (CurrentWhiteboardIndex != WhiteboardTotalCount)
                for (var i = CurrentWhiteboardIndex; i <= WhiteboardTotalCount; i++)
                    TimeMachineHistories[i] = TimeMachineHistories[i + 1];
            else
                CurrentWhiteboardIndex--;

            WhiteboardTotalCount--;

            RestoreStrokes();

            UpdateIndexInfoDisplay();

            if (WhiteboardTotalCount < 99) BtnWhiteBoardAdd.IsEnabled = true;
        }

        private bool _whiteboardModePreviousPageButtonEnabled = false;
        private bool _whiteboardModeNextPageButtonEnabled = false;
        private bool _whiteboardModeNewPageButtonEnabled = false;
        private bool _whiteboardModeNewPageButtonMerged = false;

        public bool WhiteboardModePreviousPageButtonEnabled {
            get => _whiteboardModePreviousPageButtonEnabled;
            set {
                _whiteboardModePreviousPageButtonEnabled = value;
                var geo = new GeometryDrawing[]
                    { BtnLeftWhiteBoardSwitchPreviousGeometry, BtnRightWhiteBoardSwitchPreviousGeometry };
                var label = new TextBlock[]
                    { BtnLeftWhiteBoardSwitchPreviousLabel, BtnRightWhiteBoardSwitchPreviousLabel };
                var border = new Border[]
                    { BtnWhiteBoardSwitchPreviousL, BtnWhiteBoardSwitchPreviousR };
                foreach (var gd in geo)
                    gd.Brush = new SolidColorBrush(Color.FromArgb((byte)(value ? 255 : 127), 24, 24, 27));
                foreach (var tb in label) tb.Opacity = value ? 1 : 0.5;
                foreach (var bd in border) bd.IsHitTestVisible = value;
            }
        }

        public bool WhiteboardModeNextPageButtonEnabled {
            get => _whiteboardModeNextPageButtonEnabled;
            set {
                _whiteboardModeNextPageButtonEnabled = value;
                var geo = new GeometryDrawing[]
                    { BtnLeftWhiteBoardSwitchNextGeometry, BtnRightWhiteBoardSwitchNextGeometry };
                var label = new TextBlock[]
                    { BtnLeftWhiteBoardSwitchNextLabel, BtnRightWhiteBoardSwitchNextLabel };
                var border = new Border[]
                    { BtnWhiteBoardSwitchNextL, BtnWhiteBoardSwitchNextR };
                foreach (var gd in geo)
                    gd.Brush = new SolidColorBrush(Color.FromArgb((byte)(value ? 255 : 127), 24, 24, 27));
                foreach (var tb in label) tb.Opacity = value ? 1 : 0.5;
                foreach (var bd in border) bd.IsHitTestVisible = value;
            }
        }

        public bool WhiteboardModeNewPageButtonEnabled {
            get => _whiteboardModeNewPageButtonEnabled;
            set {
                _whiteboardModeNewPageButtonEnabled = value;
                var geo = new GeometryDrawing[]
                { BtnWhiteboardAddGeometryLeft, BtnWhiteboardAddGeometryRight, BtnWhiteboardAddGeometryRightSecondary };
                var label = new TextBlock[]
                { BtnWhiteboardAddTextBlockLeft, BtnWhiteboardAddTextBlockRight, BtnWhiteboardAddTextBlockRightSecondary };
                var border = new Border[]
                { BtnWhiteboardAddLeft, BtnWhiteboardAddRight, BtnWhiteboardAddRightSecondary };
                foreach (var gd in geo)
                    gd.Brush = new SolidColorBrush(Color.FromArgb((byte)(value ? 255 : 127), 24, 24, 27));
                foreach (var tb in label) tb.Opacity = value ? 1 : 0.5;
                _whiteboardModeNewPageButtonMerged = value;
                BtnWhiteBoardSwitchNextL.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                BtnLeftPageListWB.CornerRadius = value ? new CornerRadius(0, 5, 5, 0) : new CornerRadius(0);
                BtnWhiteBoardSwitchNextR.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                BtnRightPageListWB.CornerRadius = value ? new CornerRadius(0, 5, 5, 0) : new CornerRadius(0);
                BtnWhiteboardAddRightSecondary.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                BtnWhiteboardAddRight.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            }
        }


        public bool WhiteboardModeNewPageButtonMerged {
            get => _whiteboardModeNewPageButtonMerged;
            set {
                _whiteboardModeNewPageButtonMerged = value;
                BtnWhiteBoardSwitchNextL.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                BtnWhiteBoardSwitchNextR.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                BtnWhiteboardAddRightSecondary.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                BtnWhiteboardAddRight.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                BtnRightPageListWB.CornerRadius = value ? new CornerRadius(0, 5, 5, 0) : new CornerRadius(0);
                BtnLeftPageListWB.CornerRadius = value ? new CornerRadius(0, 5, 5, 0) : new CornerRadius(0);
            }
        }

        private void UpdateIndexInfoDisplay() {
            BtnLeftPageListWBTextCount.Text =
                $"{CurrentWhiteboardIndex}/{WhiteboardTotalCount}";
            BtnRightPageListWBTextCount.Text =
                $"{CurrentWhiteboardIndex}/{WhiteboardTotalCount}";

            WhiteboardModePreviousPageButtonEnabled = CurrentWhiteboardIndex > 1;
            WhiteboardModeNextPageButtonEnabled = CurrentWhiteboardIndex < WhiteboardTotalCount;
            WhiteboardModeNewPageButtonEnabled = WhiteboardTotalCount < 99;
            WhiteboardModeNewPageButtonMerged = CurrentWhiteboardIndex == WhiteboardTotalCount;
        }

    }
}