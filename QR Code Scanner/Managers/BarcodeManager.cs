using Windows.Graphics.Imaging;
using ZXing;

namespace QR_Code_Scanner.Managers
{
    public class BarcodeManager
    {
        BarcodeReader bcReader;
        public BarcodeManager()
        {
            bcReader = new BarcodeReader();
        }
        public string DecodeBarcodeImage(SoftwareBitmap image)
        {
            var result = bcReader.Decode(image);
            if (result != null)
            {
                return result.Text;
            }
            else
            {
                return "";
            }
        }
    }
}
