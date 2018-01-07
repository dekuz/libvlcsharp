﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Java.Lang;
using Thread = Java.Lang.Thread;

namespace LibVLCSharp.Android.Sample
{
    public class AWindow : Java.Lang.Object, IVLCVout
    {
        const string Tag = "AWindow";
        const int ID_VIDEO = 0;
        const int ID_SUBTITLES = 1;
        const int ID_MAX = 2;

        class SurfaceHelper
        {
            AWindow _aWindow;
            int _id;
            readonly SurfaceView _surfaceView;
            readonly TextureView _textureView;
            static ISurfaceHolder _surfaceHolder;

            static Surface _surface;
            readonly SurfaceHolderCallback _surfaceHolderCallback;

            readonly TextureView.ISurfaceTextureListener _surfaceTextureListener;

            SurfaceHelper(int id, AWindow aWindow)
            {
                _id = id;
                _aWindow = aWindow;
                _surfaceHolderCallback = new SurfaceHolderCallback(this);
                _surfaceTextureListener = Build.VERSION.SdkInt >= BuildVersionCodes.IceCreamSandwich ? CreateSurfaceTextureListener() : null;
            }

            internal SurfaceHelper(int id, SurfaceView surfaceView, AWindow awindow) : this(id, awindow)
            {
                _surfaceView = surfaceView;
                _surfaceHolder = _surfaceView.Holder;
            }

            internal SurfaceHelper(int id, TextureView textureView, AWindow awindow) : this(id, awindow)
            {
                _textureView = textureView;
            }

            internal SurfaceHelper(int id, Surface surface, ISurfaceHolder surfaceHolder, AWindow awindow) : this(id, awindow)
            {
                _surface = surface;
                _surfaceHolder = surfaceHolder;
            }

            void SetSurface(Surface surface)
            {
                if (surface.IsValid && _aWindow.GetNativeSurface(_id) == null)
                {
                    _surface = surface;
                    _aWindow.SetNativeSurface(_id, _surface);
                    _aWindow.OnSurfaceCreated();
                }
            }

            void AttachSurfaceView()
            {
                _surfaceHolder.AddCallback(_surfaceHolderCallback);
                SetSurface(_surfaceHolder.Surface);
            }

            void AttachTextureView()
            {
                _textureView.SurfaceTextureListener = _surfaceTextureListener;
                SetSurface(new Surface(_textureView.SurfaceTexture));
            }

            void AttachSurface()
            {
                _surfaceHolder?.AddCallback(_surfaceHolderCallback);
                SetSurface(_surface);
            }

            public void Attach()
            {
                if(_surfaceView != null)
                    AttachSurfaceView();
                else if(_textureView != null)
                    AttachTextureView();
                else if (_surface != null)
                    AttachSurface();
                else throw new IllegalStateException();
            }

            void ReleaseTextureView()
            {
                if (_textureView != null)
                    _textureView.SurfaceTextureListener = null;
            }

            public void Release()
            {
                _surface = null;
                _aWindow.SetNativeSurface(_id, null);
                _surfaceHolder?.RemoveCallback(_surfaceHolderCallback);
                ReleaseTextureView();
            }

            void OnSurfaceDestroyed() => _aWindow.OnSurfaceDestroyed();

            public bool Ready => _surfaceView == null || _surface != null;

            public Surface Surface => _surface;
            
            class SurfaceHolderCallback : Java.Lang.Object, ISurfaceHolderCallback
            {
                readonly SurfaceHelper _surfaceHelper;

                internal SurfaceHolderCallback(SurfaceHelper surfaceHelper)
                {
                    _surfaceHelper = surfaceHelper;
                }
                
                public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
                {
                }

                public void SurfaceCreated(ISurfaceHolder holder)
                {
                    if (holder != _surfaceHolder)
                        throw new IllegalStateException("holders are different");
                    _surfaceHelper.SetSurface(holder.Surface);
                }

                public void SurfaceDestroyed(ISurfaceHolder holder)
                {
                    _surfaceHelper.OnSurfaceDestroyed();
                }
            }

            TextureView.ISurfaceTextureListener CreateSurfaceTextureListener()
            {
                return new SurfaceTextureListener(this);
            }

            class SurfaceTextureListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
            {
                readonly SurfaceHelper _surfaceHelper;

                internal SurfaceTextureListener(SurfaceHelper surfaceHelper)
                {
                    _surfaceHelper = surfaceHelper;
                }

                public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
                {
                    _surfaceHelper.SetSurface(new Surface(surface));
                }

                public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
                {
                    _surfaceHelper.OnSurfaceDestroyed();
                    return true;
                }

                public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
                {
                }

                public void OnSurfaceTextureUpdated(SurfaceTexture surface)
                {
                }
            }
        }
        
        static readonly object _locker = new object();

        static readonly int SURFACE_STATE_INIT = 0;
        static readonly int SURFACE_STATE_ATTACHED = 1;
        static readonly int SURFACE_STATE_READY = 2;

        SurfaceHelper[] _surfaceHelpers;
        ISurfaceCallback _surfaceCallback;
        int _surfaceState = SURFACE_STATE_INIT;
        INewVideoLayoutListener _newVideoLayoutListener;
        List<ICallback> _voutCallbacks = new List<ICallback>();
        readonly Handler _handler = new Handler(Looper.MainLooper);

        Surface[] _surfaces;
        IntPtr _callbackNativeHandle = IntPtr.Zero;
        int _mouseAction, _mouseButton, _mouseX, _mouseY, _windowWidth, _windowHeight = -1;
        SurfaceTextureThread _surfaceTextureThread = new SurfaceTextureThread();
        static readonly NativeLock _nativeLock = new NativeLock();

        public AWindow(ISurfaceCallback surfaceCallback)
        {
            _surfaceCallback = surfaceCallback;
            _surfaceHelpers = new SurfaceHelper[ID_MAX];
            _surfaces = new Surface[ID_MAX];
        }


        void EnsureInitState()
        {
            if (_surfaceState != SURFACE_STATE_INIT)
                throw new IllegalStateException($"Can't set view when already attached. Current state: {_surfaceState}," +
                                                $"_surface[ID_VIDEO]: {_surfaceHelpers[ID_VIDEO]} / {_surfaces[ID_VIDEO]}, " +
                                                $"_surfaces[ID_SUBTITLES]: {_surfaceHelpers[ID_SUBTITLES]} / {_surfaces[ID_SUBTITLES]}");
        }

        void SetView(int id, SurfaceView view)
        {
            EnsureInitState();
            if(view == null)
                throw new NullReferenceException("view is null");
            var surfaceHelper = _surfaceHelpers[id];
            surfaceHelper?.Release();

            _surfaceHelpers[id] = new SurfaceHelper(id, view, this);
        }

        void SetView(int id, TextureView view)
        {
            if(Build.VERSION.SdkInt < BuildVersionCodes.IceCreamSandwich)
                throw new IllegalArgumentException("TextureView not implemented in this android version");

            EnsureInitState();

            if(view == null)
                throw new NullReferenceException("view is null");

            var surfaceHelper = _surfaceHelpers[id];
            surfaceHelper?.Release();
            _surfaceHelpers[id] = new SurfaceHelper(id, view, this);
        }

        void SetSurface(int id, Surface surface, ISurfaceHolder surfaceHolder)
        {
            EnsureInitState();

            if (!surface.IsValid && surfaceHolder == null)
                throw new IllegalStateException("surface is not attached and holder is null");

            var surfaceHelper = _surfaceHelpers[id];
            surfaceHelper?.Release();
            _surfaceHelpers[id] = new SurfaceHelper(id, surface, surfaceHolder, this);
        }

        public void SetVideoView(SurfaceView videoSurfaceView)
        {
            SetView(ID_VIDEO, videoSurfaceView);
        }
        
        public void SetVideoView(TextureView videoTextureView)
        {
            SetView(ID_VIDEO, videoTextureView);
        }

        public void SetVideoSurface(Surface videoSurface, ISurfaceHolder surfaceHolder)
        {
            SetSurface(ID_VIDEO, videoSurface, surfaceHolder);
        }

        public void SetVideoSurface(SurfaceTexture videoSurfaceTexture)
        {
            SetSurface(ID_VIDEO, new Surface(videoSurfaceTexture), null);
        }

        public void SetSubtitlesView(SurfaceView subtitlesSurfaceView)
        {
            SetView(ID_SUBTITLES, subtitlesSurfaceView);
        }
        
        public void SetSubtitlesView(TextureView subtitlesTextureView)
        {
            SetView(ID_SUBTITLES, subtitlesTextureView);
        }

        public void SetSubtitlesSurface(Surface subtitlesSurface, ISurfaceHolder surfaceHolder)
        {
            SetSurface(ID_SUBTITLES, subtitlesSurface, surfaceHolder);
        }

        public void SetSubtitlesSurface(SurfaceTexture subtitlesSurfaceTexture)
        {
            SetSurface(ID_SUBTITLES, new Surface(subtitlesSurfaceTexture), null);
        }

        public void AttachViews(INewVideoLayoutListener onNewVideoLayoutListener)
        {
            if (_surfaceState != SURFACE_STATE_INIT || _surfaceHelpers[ID_VIDEO] == null)
                throw new IllegalStateException("Already attached or video view not configured");

            Interlocked.Exchange(ref _surfaceState, SURFACE_STATE_ATTACHED);

            lock (_nativeLock)
            {
                _newVideoLayoutListener = onNewVideoLayoutListener;
                _nativeLock.BuffersGeometryAbort = false;
                _nativeLock.BuffersGeometryConfigured = false;
            }

            for (var id = 0; id < ID_MAX; ++id)
            {
                _surfaceHelpers[id]?.Attach();
            }
        }

        public void AttachViews()
        {
            AttachViews(null);
        }

        public void DetachViews()
        {
            if (_surfaceState == SURFACE_STATE_INIT)
                return;

            Interlocked.Exchange(ref _surfaceState, SURFACE_STATE_INIT);

            _handler.RemoveCallbacksAndMessages(null);
            lock (_locker)
            {
                _newVideoLayoutListener = null;
                _nativeLock.BuffersGeometryAbort = true;
                //_nativeLock.NotifyAll()
            }
            for (var id = 0; id < ID_MAX; ++id)
            {
                _surfaceHelpers[id]?.Release();
                _surfaceHelpers[id] = null;
            }
            foreach(var cb in _voutCallbacks)
                cb.OnSurfacesDestroyed(this);
            if(_surfaceCallback != null)
                _surfaceCallback.OnSurfacesDestroyed(this);
            if(Build.VERSION.SdkInt >= BuildVersionCodes.JellyBean)
                _surfaceTextureThread.Release();
        }

        public bool AreViewsAttached => _surfaceState != SURFACE_STATE_INIT;

        void OnSurfaceCreated()
        {
            if (_surfaceState != SURFACE_STATE_ATTACHED)
                throw new IllegalArgumentException("Invalid state");

            var videoHelper = _surfaceHelpers[ID_VIDEO];
            var subtitlesHelper = _surfaceHelpers[ID_SUBTITLES];

            if (videoHelper == null)
                throw new NullReferenceException(nameof(videoHelper));

            if (videoHelper.Ready && (subtitlesHelper == null || subtitlesHelper.Ready))
            {
                Interlocked.Exchange(ref _surfaceState, SURFACE_STATE_READY);
                foreach(var cb in _voutCallbacks)
                    cb.OnSurfaceCreated(this);
                _surfaceCallback?.OnSurfacesCreated(this);
            }
        }

        void OnSurfaceDestroyed() => DetachViews();

        bool AreSurfacesWaiting => _surfaceState == SURFACE_STATE_ATTACHED;

        public void SendMouseEvent(int action, int button, int x, int y)
        {
            lock (_nativeLock)
            {
                //if(_callbackNativeHandle != 0)
            }
        }

        public void AddCallback(ICallback callback)
        {
            throw new System.NotImplementedException();
        }

        public void RemoveCallback(ICallback callback)
        {
            throw new System.NotImplementedException();
        }

       

        public void SetWindowSize(int width, int height)
        {
            throw new System.NotImplementedException();
        }

       

        Surface GetNativeSurface(int id)
        {
            lock (_nativeLock)
            {
                return _surfaces[id];
            }
        }

        void SetNativeSurface(int id, Surface surface)
        {
            lock (_nativeLock)
            {
                _surfaces[id] = surface;
            }
        }

    }

    public interface IVLCVout
    {
        /// <summary>
        /// Set a surfaceView used for video out.
        /// </summary>
        /// <param name="videoSurfaceView"></param>
        void SetVideoView(SurfaceView videoSurfaceView);

        /// <summary>
        /// Set a TextureView used for video out.
        /// </summary>
        /// <param name="videoTextureView"></param>
        void SetVideoView(TextureView videoTextureView);

        /// <summary>
        /// Set a surface used for video out.
        /// </summary>
        /// <param name="videoSurface">videoSurface if surfaceHolder is null, this surface must be valid and attached.</param>
        /// <param name="surfaceHolder">surfaceHolder optional, used to configure buffers geometry before Android ICS and to get notified when surface is destroyed.</param>
        void SetVideoSurface(Surface videoSurface, ISurfaceHolder surfaceHolder);

        /// <summary>
        /// Set a SurfaceTexture used for video out.
        /// </summary>
        /// <param name="videoSurfaceTexture">this surface must be valid and attached.</param>
        void SetVideoSurface(SurfaceTexture videoSurfaceTexture);

        /// <summary>
        /// Set a surfaceView used for subtitles out.
        /// </summary>
        /// <param name="subtitlesSurfaceView"></param>
        void SetSubtitlesView(SurfaceView subtitlesSurfaceView);

        /// <summary>
        /// Set a TextureView used for subtitles out.
        /// </summary>
        /// <param name="subtitlesTextureView"></param>
        void SetSubtitlesView(TextureView subtitlesTextureView);

        /// <summary>
        /// Set a surface used for subtitles out.
        /// </summary>
        /// <param name="subtitlesSurface">if surfaceHolder is null, this surface must be valid and attached.</param>
        /// <param name="surfaceHolder">optional, used to configure buffers geometry before Android ICS and to get notified when surface is destroyed.</param>
        void SetSubtitlesSurface(Surface subtitlesSurface, ISurfaceHolder surfaceHolder);

        /// <summary>
        /// Set a SurfaceTexture used for subtitles out.
        /// </summary>
        /// <param name="subtitlesSurfaceTexture">this surface must be valid and attached.</param>
        void SetSubtitlesSurface(SurfaceTexture subtitlesSurfaceTexture);

        /// <summary>
        /// Attach views with an OnNewVideoLayoutListener
        /// </summary>
        /// <param name="onNewVideoLayoutListener"></param>
        void AttachViews(INewVideoLayoutListener onNewVideoLayoutListener);

        void AttachViews();

        void DetachViews();

        bool AreViewsAttached { get; }

        void AddCallback(ICallback callback);

        void RemoveCallback(ICallback callback);

        void SendMouseEvent(int action, int button, int x, int y);

        void SetWindowSize(int width, int height);
    }

    public interface INewVideoLayoutListener
    {
        /**
         * This listener is called when the "android-display" "vout display" module request a new
         * video layout. The implementation should take care of changing the surface
         * LayoutsParams accordingly. If width and height are 0, LayoutParams should be reset to the
         * initial state (MATCH_PARENT).
         *
         * @param vlcVout vlcVout
         * @param width Frame width
         * @param height Frame height
         * @param visibleWidth Visible frame width
         * @param visibleHeight Visible frame height
         * @param sarNum Surface aspect ratio numerator
         * @param sarDen Surface aspect ratio denominator
         */
        void OnNewVideoLayout(IVLCVout vlcVout, int width, int height,
            int visibleWidth, int visibleHeight, int sarNum, int sarDen);
    }

    public interface ICallback
    {
        /// <summary>
        /// This callback is called when surfaces are created.
        /// </summary>
        /// <param name="vlcVout"></param>
        void OnSurfaceCreated(IVLCVout vlcVout);

        /// <summary>
        /// This callback is called when surfaces are destroyed.
        /// </summary>
        /// <param name="vlcVout"></param>
        void OnSurfacesDestroyed(IVLCVout vlcVout);
    }

    public interface ISurfaceCallback
    {
        void OnSurfacesCreated(AWindow vout);
        void OnSurfacesDestroyed(AWindow vout);
    }

    class SurfaceTextureThread : Java.Lang.Object, IRunnable, SurfaceTexture.IOnFrameAvailableListener
    {
        SurfaceTexture _surfaceTexture;
        Surface _surface;

        bool _frameAvailable;
        Looper _looper;
        Thread _thread;
        bool _isAttached;
        bool _doRelease;

        bool AttachToGLContext(int texName)
        {
            if (_surfaceTexture == null)
            {
                _thread = new Thread(this);
                _thread.Start();
                while (_surfaceTexture == null)
                {
                    try
                    {
                        _thread.Wait();
                    }
                    catch (InterruptedException)
                    {
                        return false;
                    }
                }
                _surface = new Surface(_surfaceTexture);
            }
            _surfaceTexture.AttachToGLContext(texName);
            _frameAvailable = false;
            _isAttached = true;
            return true;
        }


        public void Dispose()
        {
            
        }

        public IntPtr Handle { get; }

        public void OnFrameAvailable(SurfaceTexture surfaceTexture)
        {
            if (surfaceTexture == _surfaceTexture)
            {
                if(_frameAvailable)
                    throw new IllegalStateException("An available frame was not updated");
                _frameAvailable = true;
                _thread.Notify();
            }
        }

        readonly object _locker = new object();

        public void Run()
        {
            Looper.Prepare();

            lock (_locker)
            {
                _looper = Looper.MyLooper();
                _surfaceTexture = new SurfaceTexture(0);
                _surfaceTexture.DetachFromGLContext();
                _surfaceTexture.SetOnFrameAvailableListener(this);
                _thread.Notify();
            }

            Looper.Loop();
        }

        void DetachFromGLContext()
        {
            if (!_doRelease)
            {
                _surfaceTexture.DetachFromGLContext();
                _isAttached = false;
                return;
            }

            _looper.Quit();
            _looper = null;

            try
            {
                _thread.Join();
            }
            catch (InterruptedException) { }

            _thread = null;
            _surface.Release();
            _surface = null;
            _surfaceTexture.Release();
            _surfaceTexture = null;
            _doRelease = false;
            _isAttached = false;
        }

        bool WaitAndUpdateTexImage(float[] transformMatrix)
        {
            lock (_locker)
            {
                while (!_frameAvailable)
                {
                    try
                    {
                        _thread.Wait(500);
                        if (!_frameAvailable)
                            return false;
                    }
                    catch (InterruptedException) { }
                }
                _frameAvailable = false;
            }

            _surfaceTexture.UpdateTexImage();
            _surfaceTexture.GetTransformMatrix(transformMatrix);
            return true;
        }

        Surface Surface => _surface;

        internal void Release()
        {
            if (_surfaceTexture != null)
            {
                if (_isAttached)
                    _doRelease = true;
                else
                {
                    _surface.Release();
                    _surface = null;
                    _surfaceTexture.Release();
                    _surfaceTexture = null;
                }
            }
        }
    }

    class NativeLock
    {
        public bool BuffersGeometryConfigured;
        public bool BuffersGeometryAbort;
    }
}