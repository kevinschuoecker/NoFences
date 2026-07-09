namespace FlowGrid.Model
{
    /// <summary>
    /// Global application settings, stored next to the fence metadata.
    /// </summary>
    public class AppSettings
    {
        /* DO NOT RENAME PROPERTIES. Used for XML serialization. */

        /// <summary>
        /// When enabled, files that live inside a fence get the Hidden attribute
        /// so Explorer no longer shows them on the desktop.
        /// </summary>
        public bool HideFencedDesktopItems { get; set; }
    }
}
