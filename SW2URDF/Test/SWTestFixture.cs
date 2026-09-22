using Moq;
using SolidWorks.Interop.sldworks;
using SW2URDF.UI;
using SW2URDF.URDFExport;
using System;

namespace SW2URDF.Test
{
    /// <summary>
    /// TestFixture which gets passed to each Test Class. For now it just provides 
    /// the reference to the SolidWorks app.
    /// </summary>
    public class SWTestFixture : IDisposable
    {
        public static bool Initialized = false;
        public static SldWorks SwApp;

        public static void Initialize()
        {
            if (!Initialized)
            {
                SwApp = (SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
                SwApp.Visible = true;
                // The exporter ends every package with a modal "Creating URDF Package ..." box;
                // swap in a silent one so the SolidWorks-attached tests run unattended.
                URDFPackage.MessageBox = new Mock<IMessageBox>().Object;
                Initialized = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {

        }
    }
}
