using System;
using System.Windows.Forms;

namespace FloorplansRevitExtractor.UI
{
    /// <summary>Lets a WinForms dialog be shown modal to the main Revit window.</summary>
    internal sealed class RevitWindowHandle : IWin32Window
    {
        internal RevitWindowHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
