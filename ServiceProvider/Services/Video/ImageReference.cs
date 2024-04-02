using H264Sharp;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Videocall.Services.Video.H264;

namespace ServiceProvider.Services.Video
{
    public class ImageReference
    {
        public object underlyingData;
        public bool isManaged;
        private IntPtr dataStart;
        public byte[] Data;
        public int Offset;
        public int Length;
        public int Width, Height, Stride;
        public Action ReturnImage;
        public IntPtr DataStart {
            get
            {

                if (!isManaged)
                    return dataStart;
                else
                {
                    unsafe
                    {
                        fixed (byte* p = &Data[Offset])
                        {
                            return  (IntPtr)(p);
                        }
                    }

                }
            }
            set => dataStart = value; }

        public ImageReference(object underlyingData, IntPtr dataPtr, int width, int height, int stride, Action<ImageReference> whenReturning)
        {
            Create(underlyingData, dataPtr, width, height, stride, () => whenReturning.Invoke(this));
        }
        private void Create(object underlyingData, IntPtr dataPtr, int width, int height, int stride, Action whenReturning)
        {
            this.underlyingData = underlyingData;
            this.DataStart = dataPtr;
            Width = width;
            Height = height;
            Stride = stride;
            ReturnImage = whenReturning;
        }

        public ImageReference( byte[] data, int offset, int length, int width, int height, int stride)
        {
            Data = data;
            Offset = offset;
            Length = length;
            Width = width;
            Height = height;
            Stride = stride;
            isManaged = true;
        }

        public void Update(Mat mat)
        {
            
             Create(mat, mat.DataStart, mat.Width, mat.Height, (int)mat.Step(), ReturnImage);
        }
        internal void Update(RgbImage mat)
        {
            Create(mat, mat.ImageBytes, mat.Width, mat.Height, mat.Stride, ReturnImage);
        }
        public static ImageReference FromMat(Mat mat,Action<ImageReference> whenReturning)
        {
            return new ImageReference(mat, mat.DataStart, mat.Width, mat.Height,(int)mat.Step(), whenReturning);
        }

        public static ImageReference FromRgbImage(RgbImage rgb, Action<ImageReference> whenReturning)
        {
            return new ImageReference(rgb, rgb.ImageBytes, rgb.Width, rgb.Height, rgb.Stride, whenReturning);
        }

        public void Release()
        {
            if(underlyingData!=null && underlyingData is IDisposable)
                ((IDisposable)underlyingData).Dispose();
        }

        
    }
}
