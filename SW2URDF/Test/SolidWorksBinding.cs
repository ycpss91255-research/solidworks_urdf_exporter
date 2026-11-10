using SolidWorks.Interop.sldworks;
using System;

namespace SW2URDF.Test
{
    /// <summary>
    /// Test infrastructure: binds the integration tests to one explicitly chosen SolidWorks process
    /// instead of whichever instance the generic ProgID happens to resolve to (issue #16).
    /// </summary>
    public static class SolidWorksBinding
    {
        public const string PidEnvironmentVariable = "SW2URDF_TEST_PID";

        /// <summary>Null or blank means "not configured"; anything else must be a positive integer.</summary>
        public static int? ParseTargetPid(string value)
        {
            throw new NotImplementedException();
        }

        /// <summary>Running Object Table name of a SolidWorks process.</summary>
        public static string RotName(int pid)
        {
            throw new NotImplementedException();
        }

        public static void VerifyProcessId(int expected, int actual)
        {
            throw new NotImplementedException();
        }

        /// <summary>Connects to the SolidWorks process with this PID through the ROT. Never starts SolidWorks.</summary>
        public static SldWorks Connect(int pid)
        {
            throw new NotImplementedException();
        }
    }
}
