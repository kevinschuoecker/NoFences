using System.Collections.Generic;

namespace FlowGrid.Model
{
    /// <summary>
    /// A named tab inside a fence with its own item list.
    /// </summary>
    public class FenceTab
    {
        /* DO NOT RENAME PROPERTIES. Used for XML serialization. */

        public string Name { get; set; }

        public List<string> Files { get; set; } = new List<string>();
    }
}
