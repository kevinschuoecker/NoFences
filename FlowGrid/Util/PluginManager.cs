using FlowGrid.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace FlowGrid.Util
{
    /// <summary>
    /// Discovers widget plugins: every DLL in %LOCALAPPDATA%\FlowGrid\Plugins
    /// is scanned for public types implementing <see cref="IFlowGridWidget"/>.
    /// Note: plugins run with full trust - only install DLLs you trust.
    /// </summary>
    public static class PluginManager
    {
        private static readonly List<IFlowGridWidget> widgets = new List<IFlowGridWidget>();

        public static IReadOnlyList<IFlowGridWidget> Widgets => widgets;

        public static string PluginsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowGrid", "Plugins");

        /// <summary>A stable identifier used to persist which plugin a fence hosts.</summary>
        public static string GetId(IFlowGridWidget widget)
        {
            return widget.GetType().FullName;
        }

        public static IFlowGridWidget Find(string id)
        {
            foreach (var widget in widgets)
                if (GetId(widget) == id)
                    return widget;
            return null;
        }

        public static void LoadPlugins()
        {
            try
            {
                Directory.CreateDirectory(PluginsPath);
            }
            catch
            {
                return;
            }

            foreach (var dll in Directory.EnumerateFiles(PluginsPath, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    var found = 0;
                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (type.IsAbstract || !typeof(IFlowGridWidget).IsAssignableFrom(type))
                            continue;
                        widgets.Add((IFlowGridWidget)Activator.CreateInstance(type));
                        found++;
                    }
                    Log.Info($"Plugin loaded: {Path.GetFileName(dll)} ({found} widgets)");
                }
                catch (Exception ex)
                {
                    // Broken or incompatible plugin - skip it, keep the app alive.
                    Log.Error($"Failed to load plugin {Path.GetFileName(dll)}", ex);
                }
            }
        }
    }
}
