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
    }
}
