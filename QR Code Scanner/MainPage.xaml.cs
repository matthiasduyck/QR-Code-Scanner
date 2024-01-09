using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;


using Windows.ApplicationModel;
using Windows.UI.Popups;
using System.Diagnostics;
using ZXing.QrCode;

using Windows.Storage.Pickers;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

using Windows.ApplicationModel.DataTransfer;

using Windows.UI.Core;
using QR_Code_Scanner.Business;
using QR_Library.Managers;
using System.Collections.ObjectModel;
using QR_Library.Business;


// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace QR_Code_Scanner
{
    public sealed partial class MainPage : Page
    {
        QRCameraManager cameraManager;
        BarcodeManager barcodeManager;
        System.Threading.Timer scanningTimer;
        CancellationTokenSource qrAnalyzerCancellationTokenSource;


        private bool HasBeenDeactivated { get; set; }

        //private string lastQrSSid { get; set; }

        public MainPage()
        {
            this.InitializeComponent();
            //catchall crash handler
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new System.UnhandledExceptionEventHandler(CrashHandler);

            QrCodeDecodedDelegate handler = new QrCodeDecodedDelegate(handleQRcodeFound);
            qrAnalyzerCancellationTokenSource = new CancellationTokenSource();
            cameraManager = new QRCameraManager(PreviewControl, Dispatcher, handler, qrAnalyzerCancellationTokenSource, null);
            barcodeManager = new BarcodeManager();
            Application.Current.Suspending += Application_Suspending;
            Application.Current.Resuming += Current_Resuming;
            Application.Current.LeavingBackground += Current_LeavingBackground;
            cameraManager.EnumerateCameras(cmbCameraSelect);

            this.donateLnk.NavigateUri = new Uri("https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=C6Q6ETR8PMDUL&source=url");
            //this.donateLnkGenerate.NavigateUri = new Uri("https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=C6Q6ETR8PMDUL&source=url");
            //this.donateLnkOpen.NavigateUri = new Uri("https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=C6Q6ETR8PMDUL&source=url");
            PreviewHistoryFeature();
            if (NagwareManager.ShouldNag())
            {
                GrdNagware.Visibility = Visibility.Visible;
            }
            grdSettings.Visibility = Visibility.Collapsed;
        }

        private QrCodeEncodingOptions GetQREncodingOptions
        {
            get
            {
                return new QrCodeEncodingOptions
                {
                    DisableECI = true,
                    CharacterSet = "UTF-8",
                    Width = 512,
                    Height = 512,
                };
            }
        }

        static void CrashHandler(object sender, System.UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            Console.WriteLine("CrashHandler caught : " + e.Message);
            Console.WriteLine("Runtime terminating: {0}", args.IsTerminating);
        }

        public void ChangeAppStatus(AppStatus appStatus)
        {
            switch (appStatus)
            {
                case AppStatus.connectingToNetwork:
                    this.Status.Text = "Connecting to network.";
                    break;
                case AppStatus.scanningForQR:
                    this.Status.Text = "Looking for QR code.";
                    break;
                case AppStatus.waitingForUserInput:
                    this.Status.Text = "Waiting for user input.";
                    break;
            }
        }

        private void Current_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
        {
            Debug.WriteLine("leaving bg");
            cameraManager.StartPreviewAsync(null);
        }

        private void Current_Resuming(object sender, object e)
        {
            Debug.WriteLine("resuming");
        }



        /// <summary>
        /// TODO Method to be triggered by delegate to display message to start connecting to a network
        /// </summary>
        /// <param name="qrmessage"></param>
        public async void handleQRcodeFound(string qrmessage, bool fromScanner)
        {
            ChangeAppStatus(AppStatus.waitingForUserInput);
            //var wifiAPdata = WifiStringParser.parseWifiString(qrmessage);
            MessageDialog msgbox;
            if (string.IsNullOrEmpty(qrmessage))
            {
                msgbox = new MessageDialog("QR empty");
            }
            else
            {
                msgbox = new MessageDialog(qrmessage);
                // Add commands and set their callbacks; both buttons use the same callback function instead of inline event handlers
                msgbox.Commands.Add(new UICommand(
                    "Copy To Clipboard",
                    new UICommandInvokedHandler(this.CopyTextToClipboardHandlerAsync), qrmessage));
            }

            msgbox.Commands.Add(new UICommand(
                "Close",
                new UICommandInvokedHandler(this.CancelHandler)));

            // Set the command that will be invoked by default
            msgbox.DefaultCommandIndex = 0;

            // Set the command to be invoked when escape is pressed
            msgbox.CancelCommandIndex = 1;

            // Show the message dialog
            await msgbox.ShowAsync();
        }

        private async void CopyTextToClipboardHandlerAsync(IUICommand command)
        {
            ChangeAppStatus(AppStatus.waitingForUserInput);
            var text = command.Id as string;
            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            //enable scanning again
            this.cameraManager.ScanForQRcodes = true;
            ChangeAppStatus(AppStatus.scanningForQR);
        }

        private void CancelHandler(IUICommand command)
        {
            //enable scanning again
            this.cameraManager.ScanForQRcodes = true;
            ChangeAppStatus(AppStatus.scanningForQR);
        }

        protected async override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Debug.WriteLine("OnNavigatedFrom");
            await cameraManager.CleanupCameraAsync();
        }
        private async void Application_Suspending(object sender, SuspendingEventArgs e)
        {
            try
            {
                //Debug.WriteLine("Application Suspending");
                var deferral = e.SuspendingOperation.GetDeferral();

                if (this.scanningTimer != null) { this.scanningTimer.Dispose(); }
                this.cameraManager.ScanForQRcodes = false;
                this.qrAnalyzerCancellationTokenSource.Cancel();

                await cameraManager.CleanupCameraAsync();

                this.barcodeManager = null;
                this.cameraManager = null;
                deferral.Complete();
            }
            catch (Exception ex)
            {
            }
        }

        private void TabsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var activeTabName = ((PivotItem)(sender as Pivot).SelectedItem).Name;
            //activeTab = this.TabsView.SelectedIndex;
            if (!string.IsNullOrEmpty(activeTabName) && activeTabName == "scan")
            {
                ActivateCameraPreviewAndScan();
                this.cameraManager.ScanForQRcodes = true;
                ChangeAppStatus(AppStatus.scanningForQR);


            }
            else
            {
                DeActivateCameraPreviewAndScan();
                this.cameraManager.ScanForQRcodes = false;
                ChangeAppStatus(AppStatus.waitingForUserInput);


            }
        }
        private async void ActivateCameraPreviewAndScan()
        {
            var selected = cmbCameraSelect.SelectedItem;
            if (cameraManager != null && cameraManager.ScanForQRcodes == false && this.HasBeenDeactivated)
            {
                this.HasBeenDeactivated = false;
                if (selected != null && selected is QR_Code_Scanner.Business.ComboboxItem)
                {
                    var selectedCamera = ((QR_Code_Scanner.Business.ComboboxItem)cmbCameraSelect.SelectedItem);
                    //start cam again
                    await cameraManager.StartPreviewAsync(selectedCamera);
                }
                else
                {
                    //start cam again
                    await cameraManager.StartPreviewAsync(null);
                }
            }
        }
        private async void DeActivateCameraPreviewAndScan()
        {
            if (cameraManager != null && cameraManager.ScanForQRcodes == true && !this.HasBeenDeactivated)
            {
                this.HasBeenDeactivated = true;
                //stop cam
                cameraManager.ScanForQRcodes = false;
                try
                {
                    await cameraManager.CleanupCameraAsync();
                }
                catch (Exception)
                {
                    //todo, investigate why this fails sometimes
                }
            }
        }

        private void BtnGenerateQR_Click(object sender, RoutedEventArgs e)
        {
            //grab values
            var text = this.txtText.Text;
            
            //verify they are filled in
            if (string.IsNullOrEmpty(text))
            {
                MessageManager.ShowMessageToUserAsync("Text is empty.");
                return;
            }

            


            //create image
            var qr = new ZXing.BarcodeWriter();
            qr.Options = GetQREncodingOptions;
            qr.Format = ZXing.BarcodeFormat.QR_CODE;
            var result = qr.Write(text);
            //set as source
            this.imgQrCode.Source = result;
            //TODO NEEDED? //this.lastQrSSid = wifiData.ssid;
            //make save button visible
            this.btnSaveFile.Visibility = Visibility.Visible;
        }

        private async void BtnSaveFile_Click(object sender, RoutedEventArgs e)
        {
            var _bitmap = new RenderTargetBitmap();
            //verify they are filled in
            if (this.imgQrCode.Source == null)
            {
                MessageManager.ShowMessageToUserAsync("No image to save, please generate one first.");
                return;
            }
            await _bitmap.RenderAsync(this.imgQrCode);    //-----> This is my ImageControl.

            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            savePicker.FileTypeChoices.Add("Image", new List<string>() { ".jpg" });
            savePicker.SuggestedFileName = "QRCodeImage_" + DateTime.Now.ToString("yyyyMMddhhmmss");
            StorageFile savefile = await savePicker.PickSaveFileAsync();
            if (savefile == null)
                return;

            var pixels = await _bitmap.GetPixelsAsync();
            using (IRandomAccessStream stream = await savefile.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await
                BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
                byte[] bytes = pixels.ToArray();
                encoder.SetPixelData(BitmapPixelFormat.Bgra8,
                                        BitmapAlphaMode.Ignore,
                                        (uint)_bitmap.PixelWidth,
                                    (uint)_bitmap.PixelHeight,
                                        200,
                                        200,
                                        bytes);

                await encoder.FlushAsync();
            }
        }

        private async void CmbCameraSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            var selected = cmbCameraSelect.SelectedItem;
            if (cameraManager != null && cameraManager.ScanForQRcodes == true && selected is ComboboxItem)
            {

                //stop cam
                cameraManager.ScanForQRcodes = false;
                try
                {
                    await cameraManager.CleanupCameraAsync();
                }
                catch (Exception)
                {
                    //todo, investigate why this fails sometimes
                }
                var selectedCamera = ((ComboboxItem)cmbCameraSelect.SelectedItem);
                //start cam again
                await cameraManager.StartPreviewAsync(selectedCamera);
            }
        }

        private async void BtnOpenQRImage_ClickAsync(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");

            Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    BitmapDecoder decoder;
                    try
                    {
                        // Create the decoder from the stream
                        decoder = await BitmapDecoder.CreateAsync(stream);
                        // Get the SoftwareBitmap representation of the file
                        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                        var QRcodeResult = barcodeManager.DecodeBarcodeImage(softwareBitmap);
                        handleQRcodeFound(QRcodeResult, false);
                    }
                    catch (Exception ex)
                    {
                        MessageDialog msgbox = new MessageDialog("An error occurred: " + ex.Message);
                    }
                }
            }
            else
            {
                MessageDialog msgbox = new MessageDialog("No file selected.");

                // Set the command that will be invoked by default
                msgbox.DefaultCommandIndex = 0;

                // Set the command to be invoked when escape is pressed
                msgbox.CancelCommandIndex = 1;

                // Show the message dialog
                await msgbox.ShowAsync();
            }
        }

        private void PreviewHistoryFeature()
        {
            //create sample image
            var qr = new ZXing.BarcodeWriter();
            qr.Options = GetQREncodingOptions;
            qr.Format = ZXing.BarcodeFormat.QR_CODE;
            //var sampleQR1 = qr.Write("https://apps.microsoft.com/store/detail/qr-code-scanner-pro/9PNVRKZ7PQVN");
            //var sampleQR2 = qr.Write("https://apps.microsoft.com/store/detail/wifi-qr-code-scanner-pro/9NKJ4PT4LLJ6");


            var history = new List<HistoryQRItem>() {
                new HistoryQRItem() {TextContent="Sample history QR Code" },
                new HistoryQRItem() {TextContent="Another QR code" }
            };

            foreach(var historyItem in history)
            {
                historyItem.GetReadableText();
            }
            var historyWrapped = history.Select(x => new HistoryQRItemWrapper(x)).Reverse();
            ObservableCollection<HistoryQRItemWrapper> observableCollectionHistoryData = new ObservableCollection<HistoryQRItemWrapper>(historyWrapped);

            this.lvHistory.ItemsSource = observableCollectionHistoryData;
        }

        private void BtnCloseNagware_Click(object sender, RoutedEventArgs e)
        {
            GrdNagware.Visibility = Visibility.Collapsed;
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            Windows.System.Launcher.LaunchUriAsync(new Uri( "https://matthiasduyck.wordpress.com/qr-code-scanner/help-faq/"));
        }

        private void btnTglSettings_Click(object sender, RoutedEventArgs e)
        {
            if (this.btnTglSettings.IsChecked??false)
            {
                this.grdSettings.Visibility = Visibility.Visible;
                //todo load state of inputs from settings file if exists
            }
            else
            {
                this.grdSettings.Visibility = Visibility.Collapsed;
            }
        }

        private void lnkSettingsClear_Click(object sender, RoutedEventArgs e)
        {
            //todo, simply delete the settingsfile
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.grdSettings.Visibility = Visibility.Collapsed;
            this.btnTglSettings.IsChecked = false;
        }

        private void btnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            //todo write values to file
        }
    }
    // This wrapper is needed because the base class cannot be linked in the main page
    public class HistoryQRItemWrapper : HistoryQRItem
    {
        public HistoryQRItemWrapper(HistoryQRItem historyQRItem) : base(historyQRItem)
        {

        }
    }
}
