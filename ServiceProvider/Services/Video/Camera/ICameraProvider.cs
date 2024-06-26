﻿namespace ServiceProvider.Services.Video.Camera
{
    public interface ICameraProvider
    {
        int FrameHeight { get; set; }
        int FrameWidth { get; set; }

        void Dispose();
        bool Grab();
        void Init(int camIdx);
        bool IsOpened();
        void Open(int camIdx);
        void Release();
        bool Retrieve(out ImageReference im);
    }
}