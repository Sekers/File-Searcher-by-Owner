using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace WindowsFormsApplication1
{
    static class WinAPI
    {
        /// <summary>
        ///     Enables or disables the specified privilege on the primary access token of the current process.
        ///     Posted at https://stackoverflow.com/a/38014032/3019260 (https://stackoverflow.com/questions/37992462/how-to-set-the-owner-of-a-file-to-system/38014032#38014032)
        /// </summary>
        /// <param name="privilege">
        ///     Privilege to enable or disable.</param>
        /// <param name="enable">
        ///     True to enable the privilege, false to disable it.</param>
        /// <returns>
        ///     True if the privilege was enabled prior to the change, false if it was disabled.</returns>
        public static bool ModifyPrivilege(PrivilegeName privilege, bool enable)
        {
            LUID luid;
            if (!LookupPrivilegeValue(null, privilege.ToString(), out luid))
                throw new Win32Exception();

            using (var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.AdjustPrivileges | TokenAccessLevels.Query))
            {
                var newPriv = new TOKEN_PRIVILEGES();
                newPriv.Privileges = new LUID_AND_ATTRIBUTES[1];
                newPriv.PrivilegeCount = 1;
                newPriv.Privileges[0].Luid = luid;
                newPriv.Privileges[0].Attributes = enable ? SE_PRIVILEGE_ENABLED : 0;

                var prevPriv = new TOKEN_PRIVILEGES();
                prevPriv.Privileges = new LUID_AND_ATTRIBUTES[1];
                prevPriv.PrivilegeCount = 1;
                uint returnedBytes;

                if (!AdjustTokenPrivileges(identity.Token, false, ref newPriv, (uint)Marshal.SizeOf(prevPriv), ref prevPriv, out returnedBytes))
                    throw new Win32Exception();

                return prevPriv.PrivilegeCount == 0 ? enable /* didn't make a change */ : ((prevPriv.Privileges[0].Attributes & SE_PRIVILEGE_ENABLED) != 0);
            }
        }

        const uint SE_PRIVILEGE_ENABLED = 2;

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState,
           UInt32 BufferLengthInBytes, ref TOKEN_PRIVILEGES PreviousState, out UInt32 ReturnLengthInBytes);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        struct TOKEN_PRIVILEGES
        {
            public UInt32 PrivilegeCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1 /*ANYSIZE_ARRAY*/)]
            public LUID_AND_ATTRIBUTES[] Privileges;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public UInt32 Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }
    }

    enum PrivilegeName
    {
        SeAssignPrimaryTokenPrivilege,
        SeAuditPrivilege,
        SeBackupPrivilege,
        SeChangeNotifyPrivilege,
        SeCreateGlobalPrivilege,
        SeCreatePagefilePrivilege,
        SeCreatePermanentPrivilege,
        SeCreateSymbolicLinkPrivilege,
        SeCreateTokenPrivilege,
        SeDebugPrivilege,
        SeEnableDelegationPrivilege,
        SeImpersonatePrivilege,
        SeIncreaseBasePriorityPrivilege,
        SeIncreaseQuotaPrivilege,
        SeIncreaseWorkingSetPrivilege,
        SeLoadDriverPrivilege,
        SeLockMemoryPrivilege,
        SeMachineAccountPrivilege,
        SeManageVolumePrivilege,
        SeProfileSingleProcessPrivilege,
        SeRelabelPrivilege,
        SeRemoteShutdownPrivilege,
        SeRestorePrivilege,
        SeSecurityPrivilege,
        SeShutdownPrivilege,
        SeSyncAgentPrivilege,
        SeSystemEnvironmentPrivilege,
        SeSystemProfilePrivilege,
        SeSystemtimePrivilege,
        SeTakeOwnershipPrivilege,
        SeTcbPrivilege,
        SeTimeZonePrivilege,
        SeTrustedCredManAccessPrivilege,
        SeUndockPrivilege,
        SeUnsolicitedInputPrivilege,
    }
}
