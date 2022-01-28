﻿using System;
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
using QR_Library.Business;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using QR_Library.Data.QRTypes;
using QR_Library.Helpers;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace QR_Code_Scanner_PRO
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        QRCameraManager cameraManager;
        BarcodeManager barcodeManager;
        HistoryManager historyManager;
        QRDataManager qrDataManager;
        System.Threading.Timer scanningTimer;
        CancellationTokenSource qrAnalyzerCancellationTokenSource;

        private HistoryQRItem lastResult;
        private bool lastResultFromScanner;

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
            cameraManager = new QRCameraManager(PreviewControl, Dispatcher, handler, qrAnalyzerCancellationTokenSource);
            barcodeManager = new BarcodeManager();
            historyManager = new HistoryManager();
            qrDataManager = new QRDataManager();
            Application.Current.Suspending += Application_Suspending;
            Application.Current.Resuming += Current_Resuming;
            Application.Current.LeavingBackground += Current_LeavingBackground;
            cameraManager.EnumerateCameras(cmbCameraSelect);
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
            historyManager.AppendToHistory(qrmessage);
            lastResult = new HistoryQRItem() { TextContent = qrmessage };
            lastResultFromScanner = fromScanner;
            ChangeAppStatus(AppStatus.waitingForUserInput);

            MessageDialog msgbox;
            if (string.IsNullOrEmpty(qrmessage))
            {
                msgbox = new MessageDialog("QR empty");
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
            else
            {

                var qrType = qrDataManager.DetermineQRType(qrmessage);

                switch (qrType)
                {
                    case QRCodeType.Wifi:
                        ShowWifiResult();
                        break;
                    case QRCodeType.VCard:
                        ShowVCardResult();
                        break;
                    case QRCodeType.SMS:
                        ShowSMSResult();
                        break;
                    case QRCodeType.Email:
                        ShowEmailResult();
                        break;
                    case QRCodeType.Hyperlink:
                        ShowLinkResult();
                        break;
                    case QRCodeType.Text:
                        ShowTextResult();
                        break;
                }
            }
        }

        private void CancelHandler(IUICommand command)
        {
            //enable scanning again
            this.cameraManager.ScanForQRcodes = true;
            ChangeAppStatus(AppStatus.scanningForQR);
        }

        private void ShowTextResult()
        {
            this.GrdQRResultText.Visibility = Visibility.Visible;
            this.txtResult.Text = lastResult.TextContent;
        }

        private void ShowLinkResult()
        {
            this.GrdQRResultUrl.Visibility = Visibility.Visible;
            this.lnkQRCodeResult.NavigateUri = new Uri(lastResult.TextContent);
            this.rnLinkQRCodeResult.Text = lastResult.TextContent;
        }

        private void ShowEmailResult()
        {
            this.GrdQRResultEmail.Visibility = Visibility.Visible;
            var emailQRData = new Email(lastResult);

            this.rnEmailResultEmail.Text = "empty";
            this.rnEmailResultSubject.Text = "empty";
            this.rnEmailResultMessage.Text = "empty";

            if (!string.IsNullOrEmpty(emailQRData.ToEmailField))
            {
                this.rnEmailResultEmail.Text = emailQRData.ToEmailField;
            }

            if (!string.IsNullOrEmpty(emailQRData.SubjectEmailField))
            {
                this.rnEmailResultSubject.Text = emailQRData.SubjectEmailField;
            }

            if (!string.IsNullOrEmpty(emailQRData.MessageEmailField))
            {
                this.rnEmailResultMessage.Text = emailQRData.MessageEmailField;
            }
        }

        private void ShowSMSResult()
        {
            this.GrdQRResultSMS.Visibility = Visibility.Visible;
            var smsQRData = new SMS(lastResult);

            this.rnSMSResultMessage.Text = "empty";
            this.rnSMSResultNumber.Text = "empty";

            if (!string.IsNullOrEmpty(smsQRData.Number))
            {
                this.rnSMSResultNumber.Text = smsQRData.Number;
            }

            if (!string.IsNullOrEmpty(smsQRData.Message))
            {
                this.rnSMSResultMessage.Text = smsQRData.Message;
            }
        }

        private void ShowWifiResult()
        {
            this.GrdQRResultWifi.Visibility = Visibility.Visible;
            var wifiData = new Wifi(lastResult);

            this.rnWifiResultHidden.Text = "no";
            this.rnWifiResultNetwork.Text = "empty";
            this.rnWifiResultPassword.Text = "empty";
            this.rnWifiResultSecurity.Text = "empty";

            if (!string.IsNullOrEmpty(wifiData.Password))
            {
                this.rnWifiResultPassword.Text = wifiData.Password;
            }
            if (wifiData.Hidden)
            {
                this.rnWifiResultHidden.Text = "yes";
            }
            if (!string.IsNullOrEmpty(wifiData.Ssid))
            {
                this.rnWifiResultNetwork.Text = wifiData.Ssid;
            }
            if (wifiData.WifiAccessPointSecurityType!=null)
            {
                this.rnWifiResultSecurity.Text = Enum.GetName(typeof(WifiSecurity), wifiData.WifiAccessPointSecurityType);
            }
        }

        private void ShowVCardResult()
        {
            this.GrdQRResultVCard.Visibility = Visibility.Visible;
            var vCardQRData = new VCard(lastResult);

            this.rnVCardResultFirstName.Text = "empty";
            this.rnVCardResultLastName.Text = "empty";
            this.rnVCardResultCity.Text = "empty";
            this.rnVCardResultCompany.Text = "empty";
            this.rnVCardResultCountry.Text = "empty";
            this.rnVCardResultEmail.Text = "empty";
            this.rnVCardResultFax.Text = "empty";
            this.rnVCardResultMobile.Text = "empty";
            this.rnVCardResultPhone.Text = "empty";
            this.rnVCardResultState.Text = "empty";
            this.rnVCardResultStreet.Text = "empty";
            this.rnVCardResultTitle.Text = "empty";
            this.rnVCardResultWebsite.Text = "empty";
            this.rnVCardResultZIP.Text = "empty";

            if (!string.IsNullOrEmpty(vCardQRData.FirstName))
            {
                this.rnVCardResultFirstName.Text = vCardQRData.FirstName;
            }
            if (!string.IsNullOrEmpty(vCardQRData.LastName))
            {
                this.rnVCardResultLastName.Text = vCardQRData.LastName;
            }
            if (!string.IsNullOrEmpty(vCardQRData.City))
            {
                this.rnVCardResultCity.Text = vCardQRData.City;
            }
            if (!string.IsNullOrEmpty(vCardQRData.Company))
            {
                this.rnVCardResultCompany.Text = vCardQRData.Company;
            }
            if (!string.IsNullOrEmpty(vCardQRData.Country))
            {
                this.rnVCardResultCountry.Text = vCardQRData.Country;
            }
            if (!string.IsNullOrEmpty(vCardQRData.Email))
            {
                this.rnVCardResultEmail.Text = vCardQRData.Email;
            }
            if (!string.IsNullOrEmpty(vCardQRData.Fax))
            {
                this.rnVCardResultFax.Text = vCardQRData.Fax;
            }
            if (!string.IsNullOrEmpty(vCardQRData.Mobile))
            {
                this.rnVCardResultMobile.Text = vCardQRData.Mobile;
            }
            if (!string.IsNullOrEmpty(vCardQRData.Phone))
            {
                this.rnVCardResultPhone.Text = vCardQRData.Phone;
            }
            if (!string.IsNullOrEmpty(vCardQRData.State))
            {
                this.rnVCardResultState.Text = vCardQRData.State;
            }
            if (!string.IsNullOrEmpty(vCardQRData.Street))
            {
                this.rnVCardResultStreet.Text = vCardQRData.Street;
            }
            if (!string.IsNullOrEmpty(vCardQRData.Title))
            {
                this.rnVCardResultTitle.Text = vCardQRData.Title;
            }
            if (!string.IsNullOrEmpty(vCardQRData.Website))
            {
                this.rnVCardResultWebsite.Text = vCardQRData.Website;
            }
            if (!string.IsNullOrEmpty(vCardQRData.ZIP))
            {
                this.rnVCardResultZIP.Text = vCardQRData.ZIP;
            }


            
        }

        private async void CopyTextToClipboardHandlerAsync(IUICommand command)
        {
            ChangeAppStatus(AppStatus.waitingForUserInput);
            var text = command.Id as string;
            Helpers.CopyToClipboard(text);
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
            //Debug.WriteLine("Application Suspending");
            var deferral = e.SuspendingOperation.GetDeferral();

            this.scanningTimer.Dispose();
            this.cameraManager.ScanForQRcodes = false;
            this.qrAnalyzerCancellationTokenSource.Cancel();

            await cameraManager.CleanupCameraAsync();

            this.barcodeManager = null;
            this.cameraManager = null;
            deferral.Complete();
        }

        private void TabsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CloseAllResultPopups();
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
                if(!string.IsNullOrEmpty(activeTabName) && activeTabName == "history")
                {
                    RetrieveAndLoadHistory();
                }
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
            var options = new QrCodeEncodingOptions
            {
                DisableECI = true,
                CharacterSet = "UTF-8",
                Width = 512,
                Height = 512,
            };
            var qr = new ZXing.BarcodeWriter();
            qr.Options = options;
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

        
        private void GrdClickCapture_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // The parent Grid was click, dismiss the result window
            //CloseAllResults(null, null); this is problematic as it messes with scanning reenable

        }

        private void GrdQRResult_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Prevent click propagation to parent grid
            e.Handled = true;
        }

        private void CloseAllResultPopups()
        {
            this.GrdQRResultUrl.Visibility = Visibility.Collapsed;
            this.GrdQRResultText.Visibility = Visibility.Collapsed;
            this.GrdQRResultEmail.Visibility = Visibility.Collapsed;
            this.GrdQRResultSMS.Visibility = Visibility.Collapsed;
            this.GrdQRResultVCard.Visibility = Visibility.Collapsed;
            this.GrdQRResultWifi.Visibility = Visibility.Collapsed;
        }

        private void CloseAllResults(object sender, RoutedEventArgs e)
        {
            CloseAllResultPopups();

            if (lastResultFromScanner)
            {
                //enable scanning again
                this.cameraManager.ScanForQRcodes = true;
                ChangeAppStatus(AppStatus.scanningForQR);
            }
        }

        private void QRCopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            var specificLastResult = qrDataManager.UpgradeHistoryItem(lastResult);
            specificLastResult.GetReadableText();
            Helpers.CopyToClipboard(specificLastResult.ReadableText);
        }

        private async void RetrieveAndLoadHistory()
        {
            var history = await historyManager.RetrieveHistory();
            if (history!=null && history.Any())
            {
                history = history.Select(x=> qrDataManager.UpgradeHistoryItem(x)).ToList();
                foreach (var historyItem in history)
                {
                    historyItem.GetReadableText();
                }
                var historyWrapped = history.Select(x => new HistoryQRItemWrapper(x)).Reverse();
                ObservableCollection<HistoryQRItemWrapper> observableCollectionHistoryData = new ObservableCollection<HistoryQRItemWrapper>(historyWrapped);

                this.lvHistory.ItemsSource = observableCollectionHistoryData;
            }
        }

        private void BtnCopyHistoryText_Click(object sender, RoutedEventArgs e)
        {
            var historyQRItem = ((Windows.UI.Xaml.FrameworkElement)sender).DataContext as HistoryQRItemWrapper;
            Helpers.CopyToClipboard(historyQRItem.ReadableText);
        }
    }
    // This wrapper is needed because the base class cannot be linked in the main page
    public class HistoryQRItemWrapper : HistoryQRItem
    {
        public HistoryQRItemWrapper(HistoryQRItem historyQRItem) : base(historyQRItem) {

        }
    }
}
