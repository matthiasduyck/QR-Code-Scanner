using System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Core;
using Windows.Media.Capture;
using System.Threading.Tasks;
using Windows.System.Display;
using Windows.Graphics.Display;
using Windows.Storage.Streams;
using Windows.Media.MediaProperties;
using ZXing;
using Windows.UI.Xaml.Media.Imaging;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Windows.Devices.Enumeration;
using System.Collections.ObjectModel;
using QR_Code_Scanner.Business;
using Windows.Media.Capture.Frames;
using Windows.Media;
using Windows.Graphics.Imaging;

namespace QR_Code_Scanner.Managers
{
    public class QRCameraManager
    {
        MediaCapture mediaCapture;
        bool isPreviewing;
        DisplayRequest displayRequest = new DisplayRequest();
        CaptureElement previewWindowElement;
        CoreDispatcher dispatcher;
        CancellationTokenSource qrAnalyzerCancellationTokenSource;
        QrCodeDecodedDelegate qrCodeDecodedDelegate;
        InMemoryRandomAccessStream inMemoryRandomAccessStream;
        WriteableBitmap writeableBitmap;
        Result bcResult;
        BarcodeReader bcReader = new BarcodeReader();
        static int imgCaptureWidth = 800;
        static int imgCaptureHeight = 800;

        private IEnumerable<DeviceInformation> availableColorCameras;

        public bool ScanForQRcodes { get; set; }

        public QRCameraManager(CaptureElement previewWindowElement, CoreDispatcher dispatcher, QrCodeDecodedDelegate qrCodeDecodedDelegate, CancellationTokenSource qrAnalyzerCancellationTokenSource)
        {
            this.previewWindowElement = previewWindowElement;
            this.dispatcher = dispatcher;
            this.qrCodeDecodedDelegate = qrCodeDecodedDelegate;
            //var qrAnalyzerCancellationTokenSource = new CancellationTokenSource();
            this.qrAnalyzerCancellationTokenSource = qrAnalyzerCancellationTokenSource;
            this.inMemoryRandomAccessStream = new InMemoryRandomAccessStream();
            writeableBitmap = new WriteableBitmap(imgCaptureWidth, imgCaptureHeight);
        }

        public async Task EnumerateCameras(ComboBox comboBox)
        {
            var frameSourceInformations = await GetFrameSourceInformationAsync();
            if (frameSourceInformations != null && frameSourceInformations.Any())
            {
                foreach (var frameSourceInformation in frameSourceInformations)
                {
                    var videodevices = await GetFrameSourceGroupsAsync(frameSourceInformation);
                    foreach (var camera in videodevices)
                    {
                        comboBox.Items.Add(new ComboboxItem(camera.Name, camera.Id, frameSourceInformation));
                    }
                }
            }
        }

        public async Task<IEnumerable<DeviceInformation>> GetFrameSourceGroupsAsync(FrameSourceInformation frameSourceInformation)
        {
            try
            {
                var availableColorCamera = frameSourceInformation.MediaFrameSourceGroup.SourceInfos.Select(y => y.DeviceInformation).Distinct();
                return availableColorCamera;
            }
            catch (Exception ex)
            {
                MessageManager.ShowMessageToUserAsync("Tried to find all available color cameras but failed to do so.");
                return null;
            }
        }
        public async Task<List<FrameSourceInformation>> GetFrameSourceInformationAsync()
        {
            var frameSourceGroups = await MediaFrameSourceGroup.FindAllAsync();

            MediaFrameSourceInfo colorSourceInfo = null;

            var frameSourceInformations = new List<FrameSourceInformation>();

            foreach (var sourceGroup in frameSourceGroups)
            {
                foreach (var sourceInfo in sourceGroup.SourceInfos)
                {
                    if ((sourceInfo.MediaStreamType == MediaStreamType.VideoPreview || sourceInfo.MediaStreamType == MediaStreamType.VideoRecord)
                        && sourceInfo.SourceKind == MediaFrameSourceKind.Color)
                    {
                        colorSourceInfo = sourceInfo;
                        break;
                    }
                }
                if (colorSourceInfo != null)
                {
                    frameSourceInformations.Add(new FrameSourceInformation(sourceGroup, colorSourceInfo));
                }
            }
            return frameSourceInformations;
        }

        public async Task StartPreviewAsync(QR_Code_Scanner.Business.ComboboxItem comboboxItem)
        {
            FrameSourceInformation frameSourceInformation = new FrameSourceInformation();
            try
            {
                mediaCapture = new MediaCapture();


                var settings = new MediaCaptureInitializationSettings()
                {
                    StreamingCaptureMode = StreamingCaptureMode.Video
                };
                if (comboboxItem != null)
                {
                    settings.VideoDeviceId = comboboxItem.ID;
                    frameSourceInformation = comboboxItem.MediaFrameSourceInformation;
                }
                else
                {
                    if (availableColorCameras == null)
                    {
                        var frameSourceInformations = await GetFrameSourceInformationAsync();
                        frameSourceInformation = frameSourceInformations.First();
                        availableColorCameras = await GetFrameSourceGroupsAsync(frameSourceInformation);
                    }
                    settings.VideoDeviceId = availableColorCameras.First().Id;
                }

                qrAnalyzerCancellationTokenSource = new CancellationTokenSource();
                try
                {
                    await mediaCapture.InitializeAsync(settings);
                }
                catch (Exception ex)
                {
                    MessageManager.ShowMessageToUserAsync("Tried to initialize a color camera but failed to do so.");
                }
                List<VideoEncodingProperties> availableResolutions = null;
                try
                {
                    availableResolutions = mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview).Where(properties => properties is VideoEncodingProperties).Select(properties => (VideoEncodingProperties)properties).ToList();
                }
                catch (Exception ex)
                {
                    MessageManager.ShowMessageToUserAsync("No resolutions could be detected, trying default mode.");
                }

                VideoEncodingProperties bestVideoResolution = this.findBestResolution(availableResolutions);

                if (bestVideoResolution != null)
                {
                    await mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.VideoPreview, bestVideoResolution);
                }

                displayRequest.RequestActive();
            }
            catch (UnauthorizedAccessException)
            {
                // This will be thrown if the user denied access to the camera in privacy settings
                MessageManager.ShowMessageToUserAsync("The app was denied access to the camera");
                return;
            }

            try
            {
                this.ScanForQRcodes = true;
                previewWindowElement.Source = mediaCapture;
                await mediaCapture.StartPreviewAsync();

                isPreviewing = true;
                var imgProp = new ImageEncodingProperties
                {
                    Subtype = "BMP",
                    Width = (uint)imgCaptureWidth,
                    Height = (uint)imgCaptureHeight
                };
                var bcReader = new BarcodeReader();
                var qrCaptureInterval = 200;

                var torch = mediaCapture.VideoDeviceController.TorchControl;
                var exposureCompensationControl = mediaCapture.VideoDeviceController.ExposureCompensationControl;

                if (torch.Supported) torch.Enabled = false;
                //if (exposureCompensationControl.Supported) {
                //    var maxSupported = exposureCompensationControl.Max;
                //    var minSupported = exposureCompensationControl.Min;
                //    var middleExposure = (maxSupported + minSupported) / 2;
                //    var quarterExposure = (middleExposure + minSupported) / 2;
                //    await exposureCompensationControl.SetValueAsync(quarterExposure);
                //}

                // Get information about the preview
                var previewProperties = mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

                while (!qrAnalyzerCancellationTokenSource.IsCancellationRequested && qrAnalyzerCancellationTokenSource != null && qrAnalyzerCancellationTokenSource.Token != null)
                {
                    //try capture qr code here
                    if (ScanForQRcodes)
                    {
                        VideoFrame videoFrameFormatPlaceholder = new VideoFrame(BitmapPixelFormat.Bgra8, (int)previewProperties.Width, (int)previewProperties.Height);
                        await mediaCapture.GetPreviewFrameAsync(videoFrameFormatPlaceholder);
                        await findQRinImageAsync(bcReader, videoFrameFormatPlaceholder);
                        videoFrameFormatPlaceholder.Dispose();
                        videoFrameFormatPlaceholder = null;
                    }

                    //await Task.Delay(qrCaptureInterval, qrAnalyzerCancellationTokenSource.Token);
                    var delayTask = Task.Delay(qrCaptureInterval, qrAnalyzerCancellationTokenSource.Token);
                    var continuationTask = delayTask.ContinueWith(task => { });
                    await continuationTask;
                }
            }
            catch (System.IO.FileLoadException)
            {
                mediaCapture.CaptureDeviceExclusiveControlStatusChanged += mediaCapture_CaptureDeviceExclusiveControlStatusChanged;
            }
            catch (System.ObjectDisposedException)
            {
                Debug.WriteLine("object was disposed");
            }
            catch (Exception)
            {
                Debug.WriteLine("another exception occurred.");
            }
        }

        private async void mediaCapture_CaptureDeviceExclusiveControlStatusChanged(MediaCapture sender, MediaCaptureDeviceExclusiveControlStatusChangedEventArgs args)
        {
            if (args.Status == MediaCaptureDeviceExclusiveControlStatus.SharedReadOnlyAvailable)
            {
                MessageManager.ShowMessageToUserAsync("The camera preview can't be displayed because another app has exclusive access");
            }
            else if (args.Status == MediaCaptureDeviceExclusiveControlStatus.ExclusiveControlAvailable && !isPreviewing)
            {
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    await StartPreviewAsync(null);
                });
            }
        }

        public async Task CleanupCameraAsync()
        {
            if (mediaCapture != null)
            {
                qrAnalyzerCancellationTokenSource.Cancel();
                qrAnalyzerCancellationTokenSource.Dispose();
                if (isPreviewing)
                {
                    await mediaCapture.StopPreviewAsync();
                }
                isPreviewing = false;

                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    this.previewWindowElement.Source = null;
                    if (displayRequest != null)
                    {
                        displayRequest.RequestRelease();
                    }

                    mediaCapture.Dispose();
                    mediaCapture = null;
                });
            }
        }

        private async Task findQRinImageAsync(BarcodeReader bcReader, VideoFrame previeFrame)
        {
            //When the camera is suspending, the stream can fail
            try
            {
                bcResult = bcReader.Decode(previeFrame.SoftwareBitmap);

                if (bcResult != null)
                {
                    ScanForQRcodes = false;

                    qrCodeDecodedDelegate.Invoke(bcResult.Text);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private VideoEncodingProperties findBestResolution(List<VideoEncodingProperties> videoEncodingProperties)
        {
            if (videoEncodingProperties != null && videoEncodingProperties.Any())
            {
                //we want the highest bitrate, highest fps, with a resolution that is as square as possible, and not too small or too large
                var result = videoEncodingProperties.Where(a => (a.Width >= a.Height))//square or wider
                    .Where(b => b.Width >= 400 && b.Height >= 400)//not too small
                    .Where(c => c.Width <= 800 && c.Height <= 600)//not too large
                    .OrderBy(d => ((double)d.Width) / ((double)d.Height))//order by smallest aspect ratio(most 'square' possible)
                    .ThenBy(e => e.Width)//order by the smallest possible width
                    .ThenByDescending(f => f.Bitrate)//with the highest possible bitrate
                    .First();
                return result;
            }
            return null;
        }
    }
}
