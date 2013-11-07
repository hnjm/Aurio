﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;
using System.Diagnostics;
using AudioAlign.Audio;
using System.Windows.Documents;
using AudioAlign.Audio.Project;
using System.Windows.Media;
using System.Windows.Input;
using AudioAlign.Audio.Matching;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Collections;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;

namespace AudioAlign.WaveControls {
    [TemplatePart(Name = "PART_TimeScale", Type = typeof(TimeScale))]
    [TemplatePart(Name = "PART_TrackListBox", Type = typeof(MultiTrackListBox))]
    public class MultiTrackViewer : VirtualViewBase {

        public static readonly DependencyProperty VirtualCaretOffsetProperty;

        static MultiTrackViewer() {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MultiTrackViewer), 
                new FrameworkPropertyMetadata(typeof(MultiTrackViewer)));

            VirtualCaretOffsetProperty = CaretOverlay.VirtualCaretOffsetProperty
                .AddOwner(typeof(MultiTrackViewer), new FrameworkPropertyMetadata() { 
                    Inherits = true, CoerceValueCallback = CaretOverlay.CoerceVirtualCaretOffset 
                });
        }

        private MultiTrackListBox multiTrackListBox;
        private MultiTrackConnectionAdorner multiTrackConnectionAdorner;

        public MultiTrackViewer() {
            this.Loaded += new RoutedEventHandler(MultiTrackViewer_Loaded);
        }

        public override void OnApplyTemplate() {
            base.OnApplyTemplate();
            multiTrackListBox = (MultiTrackListBox)GetTemplateChild("PART_TrackListBox");
            multiTrackListBox.ItemsSource = new TrackList<AudioTrack>();
        }

        private void MultiTrackViewer_Loaded(object sender, RoutedEventArgs e) {
            AddHandler(CaretOverlay.PositionSelectedEvent, new CaretOverlay.PositionEventHandler(MultiTrackViewer_CaretPositionSelected));
            AddHandler(CaretOverlay.IntervalSelectedEvent, new CaretOverlay.IntervalEventHandler(MultiTrackViewer_CaretIntervalSelected));
            AddHandler(WaveView.TrackOffsetChangedEvent, new RoutedEventHandler(MultiTrackViewer_WaveViewTrackOffsetChanged));

            // HACK refresh adorner layer after every scroll event to prevent mispositioned match indicators (actually it would suffice at a "ScrollFinished" event, but that doesn't exist)
            UIUtil.FindVisualChild<ScrollBar>(multiTrackListBox).Scroll += delegate(object sender2, System.Windows.Controls.Primitives.ScrollEventArgs e2) {
                RefreshAdornerLayer();
            };

            StackPanel itemContainer = UIUtil.FindVisualChild<StackPanel>(multiTrackListBox);
            multiTrackConnectionAdorner = new MultiTrackConnectionAdorner(itemContainer, multiTrackListBox);
            AdornerLayer.GetAdornerLayer(itemContainer).Add(multiTrackConnectionAdorner);
            multiTrackConnectionAdorner.Opacity = 0.8d;
        }

        private void MultiTrackViewer_CaretPositionSelected(object sender, CaretOverlay.PositionEventArgs e) {
            //Debug.WriteLine("MultiTrackViewer CaretPositionSelected @ " + e.Position);
            SetValue(VirtualCaretOffsetProperty, PhysicalToVirtualIntervalOffset(VirtualViewportInterval, e.SourceInterval, e.Position));
            e.Handled = true;
        }

        private void MultiTrackViewer_CaretIntervalSelected(object sender, CaretOverlay.IntervalEventArgs e) {
            //Debug.WriteLine("MultiTrackViewer CaretIntervalSelected {0} -> {1} ", e.From, e.To);
            e.Handled = true;
        }

        private void MultiTrackViewer_WaveViewTrackOffsetChanged(object sender, RoutedEventArgs e) {
            RefreshAdornerLayer();
        }

        public long VirtualCaretOffset {
            get { return (long)GetValue(VirtualCaretOffsetProperty); }
            set { SetValue(VirtualCaretOffsetProperty, value); }
        }

        public TrackList<AudioTrack> ItemsSource {
            get { return (TrackList<AudioTrack>)multiTrackListBox.ItemsSource; }
            set { multiTrackListBox.ItemsSource = value; }
        }

        public object SelectedItem {
            get { return multiTrackListBox.SelectedItem; }
        }

        public Collection<Match> Matches {
            get { return multiTrackConnectionAdorner.Matches; }
        }

        public Match SelectedMatch {
            get { return multiTrackConnectionAdorner.SelectedMatch; }
            set { multiTrackConnectionAdorner.SelectedMatch = value; }
        }

        public Collection<Match> SelectedMatches {
            get { return multiTrackConnectionAdorner.SelectedMatches; }
        }

        public bool DisplayMatches {
            get { return multiTrackConnectionAdorner.IsVisible; }
            set { multiTrackConnectionAdorner.Visibility = (value == true) ? Visibility.Visible : Visibility.Hidden; }
        }

        public bool DisplayTrackHeaders {
            get { return multiTrackListBox.TrackHeadersVisibility == Visibility.Visible; }
            set { multiTrackListBox.TrackHeadersVisibility = (value == true) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public void RefreshAdornerLayer() {
            if (multiTrackConnectionAdorner != null) {
                // HACK DispatcherPriority is a workaround - without the Dispatcher it sometimes wouldn't refresh
                Dispatcher.BeginInvoke((Action)delegate {
                    multiTrackConnectionAdorner.InvalidateVisual();
                }, DispatcherPriority.Render);
            }
        }

        public void Display(TimeSpan time, bool adjustCaret) {
            VirtualViewportOffset = time.Ticks - VirtualViewportWidth / 2;
            if (adjustCaret) {
                VirtualCaretOffset = time.Ticks;
            }
        }

        public void Display(Match m) {
            int i1 = multiTrackListBox.Items.IndexOf(m.Track1);
            int i2 = multiTrackListBox.Items.IndexOf(m.Track2);
            multiTrackListBox.ScrollIntoView(i1 < i2 ? m.Track1 : m.Track2);
        }

        public void FitTracksVertically(double minHeight) {
            double targetHeight = 
                (multiTrackListBox.ActualHeight - UIUtil.FindVisualChild<TimeScale>(multiTrackListBox).ActualHeight) / 
                multiTrackListBox.Items.Count;
            targetHeight = Math.Max(targetHeight, minHeight);
            foreach (AudioTrack track in multiTrackListBox.Items) {
                ListBoxItem listBoxItem = multiTrackListBox.ItemContainerGenerator.ContainerFromItem(track) as ListBoxItem;
                if (listBoxItem != null) {
                    ResizeDecorator resizeDecorator = UIUtil.FindVisualChild<ResizeDecorator>(listBoxItem);
                    resizeDecorator.Height = targetHeight;
                }
            }
        }

        public void CopyToClipboard(bool fullHeight) {
            ScrollViewer sv = UIUtil.FindVisualChild<ScrollViewer>(multiTrackListBox);
            //sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            multiTrackListBox.ControlsVisibility = Visibility.Hidden;

            Size size = fullHeight ? new Size(multiTrackListBox.ActualWidth + sv.ScrollableWidth, multiTrackListBox.ActualHeight + sv.ScrollableHeight) : multiTrackListBox.RenderSize;
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            multiTrackListBox.Measure(size);
            multiTrackListBox.Arrange(new Rect(size));
            rtb.Render(multiTrackListBox);

            Clipboard.SetImage(rtb);
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
            multiTrackListBox.ControlsVisibility = Visibility.Visible;
        }

        /// <summary>
        /// Preview event is used because the bubbling mousewheel event (which is already handled at this time)
        /// arrives after the listbox has done it's work on the event - which we want to avoid.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnPreviewMouseWheel(System.Windows.Input.MouseWheelEventArgs e) {
            base.OnPreviewMouseWheel(e);

            // if the ctrl-key is held, we scroll the viewport
            if ((Keyboard.Modifiers & ModifierKeys.Control) > 0) {
                long offset = 0;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) > 0) {
                    offset = (long) (VirtualViewportWidth*0.9);
                    if (e.Delta < 0) offset *= -1;
                }
                else {
                    double scrollPercentage = 0.01d;
                    double scrollOffset = ActualWidth*scrollPercentage*(e.Delta/Mouse.MouseWheelDeltaForOneLine);
                    offset = PhysicalToVirtualOffset(scrollOffset);
                }
                VirtualViewportOffset += offset;
                e.Handled = true;
                return;
            }
            // else the viewport gets zoomed

            //Debug.WriteLine("MultiTrackViewer OnPreviewMouseWheel: " + e.Delta + " (" + e.Delta / Mouse.MouseWheelDeltaForOneLine + " lines)");

            // add/remove percentage for a zoom command
            double scalePercentage = 0.20d;
            bool zoomToCaret = true;

            Interval currentViewportInterval = VirtualViewportInterval;

            // calculate new viewport width
            long newViewportWidth = (long)(e.Delta < 0 ?
                currentViewportInterval.Length * (1 + scalePercentage) :
                currentViewportInterval.Length * (1 - scalePercentage));
            //Debug.WriteLine("MultiTrackViewer viewport width change: {0} -> {1}", currentViewportWidth, newViewportWidth);
            VirtualViewportWidth = newViewportWidth; // force coercion
            newViewportWidth = VirtualViewportWidth; // get coerced value
            
            // calculate new viewport offset (don't care about the valid offset range here - it's handled by the property value coercion)
            long viewportWidthDelta = currentViewportInterval.Length - newViewportWidth;
            long newViewportOffset;
            if (zoomToCaret) {
                // zoom the viewport and move it towards the caret
                long caretPosition = VirtualCaretOffset;
                if (!currentViewportInterval.Contains(caretPosition)) {
                    // if caret is out of the viewport, just skip there
                    newViewportOffset = caretPosition - newViewportWidth / 2;
                }
                else {
                    // if caret is visible, approach it smoothly
                    // TODO simplify the following calculation
                    newViewportOffset = currentViewportInterval.From + viewportWidthDelta / 2;
                    long caretTargetPosition = newViewportOffset + newViewportWidth / 2;
                    long caretPositionsDelta = caretPosition - caretTargetPosition;
                    newViewportOffset += caretPositionsDelta / 2;
                }
            }
            else {
                // straight viewport zoom
                newViewportOffset = VirtualViewportOffset + viewportWidthDelta / 2;
            }

            // set new values
            VirtualViewportOffset = newViewportOffset;
            VirtualViewportWidth = newViewportWidth;

            e.Handled = true;
        }

        protected override void OnViewportOffsetChanged(long oldValue, long newValue) {
            base.OnViewportOffsetChanged(oldValue, newValue);
            RefreshAdornerLayer();
        }

        protected override void OnViewportWidthChanged(long oldValue, long newValue) {
            base.OnViewportWidthChanged(oldValue, newValue);
            RefreshAdornerLayer();
        }
    }
}
