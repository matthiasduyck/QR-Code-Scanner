using Windows.Media.Capture.Frames;

namespace QR_Code_Scanner.Business
{
    public class FrameSourceInformation
    {
        public MediaFrameSourceGroup MediaFrameSourceGroup { get; set; }
        public MediaFrameSourceInfo MediaFrameSourceInfo { get; set; }

        public FrameSourceInformation(MediaFrameSourceGroup mediaFrameSourceGroup, MediaFrameSourceInfo mediaFrameSourceInfo)
        {
            this.MediaFrameSourceGroup = mediaFrameSourceGroup;
            this.MediaFrameSourceInfo = mediaFrameSourceInfo;
        }

        public FrameSourceInformation()
        {

        }
    }
}
