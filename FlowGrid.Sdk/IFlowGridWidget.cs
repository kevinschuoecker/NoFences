using System;
using System.Collections.Generic;
using System.Drawing;

namespace FlowGrid.Sdk
{
    /// <summary>
    /// Implement this interface in a class library, drop the DLL into
    /// %LOCALAPPDATA%\FlowGrid\Plugins and FlowGrid offers your widget
    /// under tray menu → "New widget".
    /// </summary>
    public interface IFlowGridWidget
    {
        /// <summary>Display name shown in the "New widget" menu and as the default fence title.</summary>
        string Name { get; }

        /// <summary>How often (milliseconds) the widget should be redrawn. 0 or less = only on demand.</summary>
        int RefreshIntervalMs { get; }

        /// <summary>
        /// Draws the widget. Called on the UI thread on every refresh.
        /// </summary>
        /// <param name="g">Target graphics, anti-aliasing already enabled.</param>
        /// <param name="area">The content area below the fence title bar.</param>
        /// <param name="host">Access to fence styling (accent color, font).</param>
        void Render(Graphics g, Rectangle area, IWidgetHost host);
    }

    /// <summary>
    /// SDK v2: widgets that additionally handle mouse clicks. FlowGrid detects
    /// this interface automatically; v1 widgets keep working unchanged.
    /// </summary>
    public interface IFlowGridWidget2 : IFlowGridWidget
    {
        /// <summary>
        /// Called on the UI thread when the user left-clicks inside the widget area.
        /// </summary>
        /// <param name="location">Click position in window coordinates (same space as <paramref name="area"/>).</param>
        /// <param name="area">The current content area, identical to what Render receives.</param>
        /// <param name="host">Access to fence styling and per-fence settings.</param>
        /// <returns>true to trigger an immediate repaint.</returns>
        bool OnClick(Point location, Rectangle area, IWidgetHost host);
    }

    /// <summary>
    /// SDK v3: widgets that additionally contribute entries to the fence's
    /// right-click menu (e.g. "Add symbol...", "Set location...").
    /// </summary>
    public interface IFlowGridWidget3 : IFlowGridWidget2
    {
        /// <summary>
        /// Called on the UI thread every time the fence context menu opens.
        /// Return the items to append (null or empty for none).
        /// </summary>
        IList<WidgetMenuItem> GetMenuItems(IWidgetHost host);
    }

    /// <summary>
    /// SDK v4: widgets that host a real WinForms control inside the fence instead
    /// of drawing. Unlocks tables (DataGridView), forms, WebView2 and anything
    /// else WinForms can do. Render() is only used as a fallback (e.g. while the
    /// control could not be created) and may be empty.
    /// </summary>
    public interface IFlowGridControlWidget : IFlowGridWidget
    {
        /// <summary>
        /// Called once per hosting fence - return a NEW control instance each time
        /// so multiple fences of the same widget stay independent. The fence docks
        /// the control into its content area and disposes it when it closes.
        /// </summary>
        System.Windows.Forms.Control CreateControl(IWidgetHost host);
    }

    /// <summary>
    /// A context menu entry contributed by a widget.
    /// </summary>
    public class WidgetMenuItem
    {
        public string Text { get; set; }

        public Action OnClick { get; set; }

        public WidgetMenuItem()
        {
        }

        public WidgetMenuItem(string text, Action onClick)
        {
            Text = text;
            OnClick = onClick;
        }
    }

    /// <summary>
    /// Styling information and per-fence storage provided by the hosting fence.
    /// </summary>
    public interface IWidgetHost
    {
        /// <summary>The fence's custom tint color (black if none is set).</summary>
        Color AccentColor { get; }

        /// <summary>The font used for regular fence text.</summary>
        Font BaseFont { get; }

        /// <summary>
        /// Free-form settings string persisted with the hosting fence. Widgets can
        /// store per-fence state here (e.g. which sections are visible); assigning
        /// a value saves it immediately. Empty string when nothing was stored yet.
        /// </summary>
        string Settings { get; set; }

        /// <summary>
        /// Shows a small text input dialog (SDK v3). Must be called on the UI thread
        /// (e.g. from OnClick or a menu item). Returns the entered text, or null if
        /// the user cancelled.
        /// </summary>
        string PromptText(string title, string description, string initialValue);

        /// <summary>
        /// Requests a repaint of the hosting fence (SDK v3). Safe to call from any
        /// thread - use it when a background fetch finishes so new data shows
        /// immediately instead of waiting for the next refresh tick.
        /// </summary>
        void RequestRefresh();

        /// <summary>
        /// Reads a secret stored via <see cref="SetSecret"/> (SDK v4). Returns null
        /// if no such secret exists. Secrets are scoped to the widget type.
        /// </summary>
        string GetSecret(string name);

        /// <summary>
        /// Stores a secret (API tokens, passwords) encrypted with Windows DPAPI,
        /// bound to the current Windows user (SDK v4). Never lands in plain text
        /// and is not part of layout exports. Pass null or empty to delete.
        /// </summary>
        void SetSecret(string name, string value);
    }
}
