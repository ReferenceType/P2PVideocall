namespace ServiceProvider.Services.Video.Camera
{
    public interface ICameraProvider
    {
        int FrameHeight { get; set; }
        int FrameWidth { get; set; }

        void Dispose();
        bool Grab();
        bool IsOpened();
        void Open(int camIdx);
        void Release();
        bool Retrieve(ref ImageReference im);
    }
}