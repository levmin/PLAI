using System;
using System.Runtime.InteropServices;

namespace PLAI.Interop
{
    internal static class DxgiInterop
    {
        private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 0x1;

        [DllImport("dxgi.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

        [ComImport]
        [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIFactory1
        {
            // HRESULT EnumAdapters1(UINT Adapter, IDXGIAdapter1** ppAdapter);
            [PreserveSig]
            int EnumAdapters1(uint adapter, out IntPtr ppAdapter);

            // rest of methods not needed
        }

        /// <summary>
        /// Attempts to find the non-software adapter with the maximum dedicated video memory.
        /// Returns a tuple where:
        /// - dxgiInitialized: false if DXGI factory creation failed (treat as unknown)
        /// - foundNonSoftware: true if at least one non-software adapter was enumerated and described
        /// - maxDedicatedBytes: maximum DedicatedVideoMemory among non-software adapters (0 if none or zero)
        /// Method never throws.
        /// </summary>
        public static (bool dxgiInitialized, bool foundNonSoftware, ulong maxDedicatedBytes) TryGetMaxDedicatedVideoMemoryBytes()
        {
            IntPtr factoryPtr = IntPtr.Zero;
            try
            {
                Guid factoryGuid = typeof(IDXGIFactory1).GUID;
                int hr = CreateDXGIFactory1(ref factoryGuid, out factoryPtr);
                if (hr != 0 || factoryPtr == IntPtr.Zero)
                {
                    return (false, false, 0);
                }

                var factory = (IDXGIFactory1)Marshal.GetTypedObjectForIUnknown(factoryPtr, typeof(IDXGIFactory1));

                uint index = 0;
                bool foundNonSoftware = false;
                ulong maxDedicated = 0;

                while (true)
                {
                    IntPtr adapterPtr = IntPtr.Zero;
                    int enumHr = factory.EnumAdapters1(index, out adapterPtr);
                    if (enumHr != 0 || adapterPtr == IntPtr.Zero)
                    {
                        break;
                    }

                    try
                    {
                        var adapter = (IDXGIAdapter1)Marshal.GetTypedObjectForIUnknown(adapterPtr, typeof(IDXGIAdapter1));
                        DXGI_ADAPTER_DESC1 desc;
                        int descHr = adapter.GetDesc1(out desc);
                        if (descHr == 0)
                        {
                            if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) == 0)
                            {
                                foundNonSoftware = true;
                                // DedicatedVideoMemory is stored in DedicatedVideoMemory (UIntPtr)
                                ulong dedicated = (ulong)desc.DedicatedVideoMemory.ToUInt64();
                                if (dedicated > maxDedicated)
                                {
                                    maxDedicated = dedicated;
                                }
                            }
                        }
                    }
                    finally
                    {
                        try { Marshal.Release(adapterPtr); } catch { }
                    }

                    index++;
                }

                return (true, foundNonSoftware, maxDedicated);
            }
            catch
            {
                return (false, false, 0);
            }
            finally
            {
                if (factoryPtr != IntPtr.Zero)
                {
                    try { Marshal.Release(factoryPtr); } catch { }
                }
            }
        }

        [ComImport]
        [Guid("29038f61-3839-4626-91fd-086879011a05")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter1
        {
            // HRESULT GetDesc1(DXGI_ADAPTER_DESC1* pDesc);
            [PreserveSig]
            int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);

            // rest not needed
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;

            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public long AdapterLuid; // LUID represented as 8 bytes
            public uint Flags;
        }

        /// <summary>
        /// Try to detect whether a discrete (non-software) adapter exists.
        /// Returns true if one is found, false if not, and null on failure to initialize DXGI.
        /// Method never throws.
        /// </summary>
        public static bool? TryFindDiscreteAdapter()
        {
            IntPtr factoryPtr = IntPtr.Zero;
            try
            {
                Guid factoryGuid = typeof(IDXGIFactory1).GUID;
                int hr = CreateDXGIFactory1(ref factoryGuid, out factoryPtr);
                if (hr != 0 || factoryPtr == IntPtr.Zero)
                {
                    return null;
                }

                var factory = (IDXGIFactory1)Marshal.GetTypedObjectForIUnknown(factoryPtr, typeof(IDXGIFactory1));

                uint index = 0;
                while (true)
                {
                    IntPtr adapterPtr = IntPtr.Zero;
                    int enumHr = factory.EnumAdapters1(index, out adapterPtr);
                    if (enumHr != 0 || adapterPtr == IntPtr.Zero)
                    {
                        break;
                    }

                    try
                    {
                        var adapter = (IDXGIAdapter1)Marshal.GetTypedObjectForIUnknown(adapterPtr, typeof(IDXGIAdapter1));
                        DXGI_ADAPTER_DESC1 desc;
                        int descHr = adapter.GetDesc1(out desc);
                        if (descHr == 0)
                        {
                            // Ignore software adapters
                            if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) == 0)
                            {
                                // We found a non-software adapter. Consider it discrete.
                                return true;
                            }
                        }
                    }
                    finally
                    {
                        // release adapter COM pointer
                        try { Marshal.Release(adapterPtr); } catch { }
                    }

                    index++;
                }

                return false;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (factoryPtr != IntPtr.Zero)
                {
                    try { Marshal.Release(factoryPtr); } catch { }
                }
            }
        }
    }
}
