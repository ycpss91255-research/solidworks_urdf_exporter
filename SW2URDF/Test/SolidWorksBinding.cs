using SolidWorks.Interop.sldworks;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace SW2URDF.Test
{
    /// <summary>
    /// Test infrastructure: binds the integration tests to one explicitly chosen SolidWorks process
    /// instead of whichever instance the generic ProgID happens to resolve to (issue #16).
    /// </summary>
    public static class SolidWorksBinding
    {
        public const string PidEnvironmentVariable = "SW2URDF_TEST_PID";

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx context);

        /// <summary>Null or blank means "not configured"; anything else must be a positive integer.</summary>
        public static int? ParseTargetPid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0)
            {
                throw new ArgumentException(PidEnvironmentVariable + " must be a positive process id, but was '" +
                    value + "'. Unset it to use the generic SolidWorks ProgID instead.");
            }
            return pid;
        }

        /// <summary>Running Object Table name of a SolidWorks process.</summary>
        public static string RotName(int pid)
        {
            return "SolidWorks_PID_" + pid.ToString(CultureInfo.InvariantCulture);
        }

        public static void VerifyProcessId(int expected, int actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException("The SolidWorks object registered as " + RotName(expected) +
                    " reports process id " + actual + " instead of " + expected + "; refusing to use it.");
            }
        }

        /// <summary>Connects to the SolidWorks process with this PID through the ROT. Never starts SolidWorks.</summary>
        public static SldWorks Connect(int pid)
        {
            string wanted = RotName(pid);
            GetRunningObjectTable(0, out IRunningObjectTable table);
            CreateBindCtx(0, out IBindCtx context);
            table.EnumRunning(out IEnumMoniker monikers);
            IMoniker[] next = new IMoniker[1];
            while (monikers.Next(1, next, IntPtr.Zero) == 0)
            {
                next[0].GetDisplayName(context, null, out string name);
                if (name != wanted)
                {
                    continue;
                }
                table.GetObject(next[0], out object instance);
                SldWorks app = (SldWorks)instance;
                VerifyProcessId(pid, app.GetProcessID());
                return app;
            }
            throw new InvalidOperationException("No running SolidWorks is registered as " + wanted +
                " in the Running Object Table (" + PidEnvironmentVariable + "=" + pid +
                "). Start that instance first; the tests will not fall back to another one.");
        }

        /// <summary>
        /// The SolidWorks instance for the tests: the one named by <see cref="PidEnvironmentVariable"/> when it
        /// is set (never any other), otherwise a new or running instance from the generic ProgID as before.
        /// </summary>
        public static SldWorks GetApplication()
        {
            int? pid = ParseTargetPid(System.Environment.GetEnvironmentVariable(PidEnvironmentVariable));
            if (pid.HasValue)
            {
                return Connect(pid.Value);
            }
            return (SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
        }
    }
}
