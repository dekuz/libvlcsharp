﻿using System;
using System.Runtime.InteropServices;
using System.Security;

namespace VideoLAN.LibVLC.Manual
{
    /// <summary>
    /// libvlc v3 check
    /// </summary>
    public class MediaDiscoverer : Internal
    {
        MediaDiscovererEventManager _eventManager;
        MediaList _mediaList;

        struct Native
        {
            [SuppressUnmanagedCodeSecurity]
            [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl,
                EntryPoint = "libvlc_media_discoverer_new")]
            internal static extern IntPtr LibVLCMediaDiscovererNew(IntPtr instance, [MarshalAs(UnmanagedType.LPStr)] string name);

            [SuppressUnmanagedCodeSecurity]
            [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl,
                EntryPoint = "libvlc_media_discoverer_start")]
            internal static extern int LibVLCMediaDiscovererStart(IntPtr mediaDiscoverer);

            [SuppressUnmanagedCodeSecurity]
            [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl,
                EntryPoint = "libvlc_media_discoverer_stop")]
            internal static extern void LibVLCMediaDiscovererStop(IntPtr mediaDiscoverer);

            [SuppressUnmanagedCodeSecurity]
            [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl,
                EntryPoint = "libvlc_media_discoverer_release")]
            internal static extern void LibVLCMediaDiscovererRelease(IntPtr mediaDiscoverer);

            [SuppressUnmanagedCodeSecurity]
            [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl,
                EntryPoint = "libvlc_media_discoverer_localized_name")]
            internal static extern IntPtr LibVLCMediaDiscovererLocalizedName(IntPtr mediaDiscoverer);
            
            [SuppressUnmanagedCodeSecurity]
            [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl,
                EntryPoint = "libvlc_media_discoverer_event_manager")]
            internal static extern IntPtr LibVLCMediaDiscovererEventManager(IntPtr mediaDiscoverer);

            [SuppressUnmanagedCodeSecurity]
            [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl,
                            EntryPoint = "libvlc_media_discoverer_is_running")]
            internal static extern int LibVLCMediaDiscovererIsRunning(IntPtr mediaDiscoverer);

            [SuppressUnmanagedCodeSecurity]
            [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl,
                EntryPoint = "libvlc_media_discoverer_media_list")]
            internal static extern IntPtr LibVLCMediaDiscovererMediaList(IntPtr discovererMediaList);
        }

        /// <summary>Category of a media discoverer</summary>
        /// <remarks>libvlc_media_discoverer_list_get()</remarks>
        public enum Category
        {
            /// <summary>devices, like portable music player</summary>
            Devices = 0,
            /// <summary>LAN/WAN services, like Upnp, SMB, or SAP</summary>
            Lan = 1,
            /// <summary>Podcasts</summary>
            Podcasts = 2,
            /// <summary>Local directories, like Video, Music or Pictures directories</summary>
            Localdirs = 3
        }

        public struct Description
        {
            public Description(string name, string longName, Category category)
            {
                Name = name;
                LongName = longName;
                Category = category;
            }

            public string Name { get; }
            public string LongName { get; }
            public Category Category { get; }
        }

        public MediaDiscoverer(Instance instance, string name) 
            //v3 check. differen ctors
            : base(() => Native.LibVLCMediaDiscovererNew(instance.NativeReference, name), Native.LibVLCMediaDiscovererRelease)
        {
        }

        /// <summary>
        /// Start media discovery.
        /// To stop it, call MediaDiscover::stop() or destroy the object directly.
        /// </summary>
        /// <returns>false in case of error, true otherwise</returns>
        public bool Start() => Native.LibVLCMediaDiscovererStart(NativeReference) == 0;

        /// <summary>
        /// Stop media discovery.
        /// </summary>
        public void Stop() => Native.LibVLCMediaDiscovererStop(NativeReference);

        /// <summary>
        /// Get media service discover object its localized name.
        /// under v3 only
        /// </summary>
        public string LocalizedName => (string) Utf8StringMarshaler.GetInstance()
            .MarshalNativeToManaged(Native.LibVLCMediaDiscovererLocalizedName(NativeReference));

        /// <summary>
        /// Get event manager from media service discover object.
        /// under v3 only
        /// </summary>
        public MediaDiscovererEventManager EventManager
        {
            get
            {
                if (_eventManager == null)
                {
                    var ptr = Native.LibVLCMediaDiscovererEventManager(NativeReference);
                    if (ptr == IntPtr.Zero) return null;
                    _eventManager = new MediaDiscovererEventManager(ptr);
                }
                return _eventManager;
            }
        }

        /// <summary>
        /// Query if media service discover object is running.
        /// </summary>
        public bool IsRunning => Native.LibVLCMediaDiscovererIsRunning(NativeReference) != 0;

        public MediaList MediaList
        {
            get
            {
                if (_mediaList == null)
                {
                    var ptr = Native.LibVLCMediaDiscovererMediaList(NativeReference);
                    if (ptr == IntPtr.Zero) return null;
                    _mediaList = new MediaList(ptr);
                }
                return _mediaList;
            }
        }
    }
}
