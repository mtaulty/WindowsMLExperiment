//#define RESIZE
namespace App1
{
    using daschunds.model;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Windows.Devices.Enumeration;
    using Windows.Graphics.Imaging;
    using Windows.Media.Capture;
    using Windows.Media.Capture.Frames;
    using Windows.Media.Devices;
    using Windows.Storage;
    using Windows.Storage.Streams;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media.Imaging;
    using Windows.Media;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using Windows.UI.Core;
    using System.Runtime.InteropServices.WindowsRuntime;

    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public MainPage()
        {
            this.InitializeComponent();
            this.inputData = new DacshundModelInput();
            this.Loaded += OnLoaded;
        }
        public string Dog
        {
            get => this.dog;
            set => this.SetProperty(ref this.dog, value);
        }
        string dog;
        public string Pony
        {
            get => this.pony;
            set => this.SetProperty(ref this.pony, value);
        }
        string pony;
        public string Dacshund
        {
            get => this.daschund;
            set => this.SetProperty(ref this.daschund, value);
        }
        string daschund;
        public string Category
        {
            get => this.category;
            set => this.SetProperty(ref this.category, value);
        }
        string category;
        async Task LoadModelAsync()
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(
                new Uri("ms-appx:///Model/daschunds.onnx"));

            this.learningModel = await DacshundModel.CreateDaschundModel(file);
        }
        async Task<DeviceInformation> GetFirstBackPanelVideoCaptureAsync()
        {
            var devices = await DeviceInformation.FindAllAsync(
                DeviceClass.VideoCapture);

            var device = devices.FirstOrDefault(
                d => d.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Back);

            return (device);
        }
        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await this.LoadModelAsync();

            var device = await this.GetFirstBackPanelVideoCaptureAsync();

            if (device != null)
            {
                await this.CreateMediaCaptureAsync(device);
                await this.mediaCapture.StartPreviewAsync();

                await this.CreateMediaFrameReaderAsync();
                await this.frameReader.StartAsync();
            }
        }

        async Task CreateMediaFrameReaderAsync()
        {
            var frameSource = this.mediaCapture.FrameSources.Where(
                source => source.Value.Info.SourceKind == MediaFrameSourceKind.Color).First();

            this.frameReader =
                await this.mediaCapture.CreateFrameReaderAsync(frameSource.Value);

            this.frameReader.FrameArrived += OnFrameArrived;
        }

        async Task CreateMediaCaptureAsync(DeviceInformation device)
        {
            this.mediaCapture = new MediaCapture();

            await this.mediaCapture.InitializeAsync(
                new MediaCaptureInitializationSettings()
                {
                    VideoDeviceId = device.Id
                }
            );
            // Try and set auto focus but on the Surface Pro 3 I'm running on, this
            // won't work.
            if (this.mediaCapture.VideoDeviceController.FocusControl.Supported)
            {
                await this.mediaCapture.VideoDeviceController.FocusControl.SetPresetAsync(FocusPreset.AutoNormal);
            }
            else
            {
                // Nor this.
                this.mediaCapture.VideoDeviceController.Focus.TrySetAuto(true);
            }
            this.captureElement.Source = this.mediaCapture;
        }

        async void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            if (Interlocked.CompareExchange(ref this.processingFlag, 1, 0) == 0)
            {
                try
                {
                    using (var frame = sender.TryAcquireLatestFrame())
                    using (var videoFrame = frame.VideoMediaFrame?.GetVideoFrame())
                    {
                        if (videoFrame != null)
                        {
                            // From the description (both visible in Python and through the
                            // properties of the model that I can interrogate with code at
                            // runtime here) my image seems to to be 227 by 227 which is an 
                            // odd size but I'm assuming that I should resize the frame here to 
                            // suit that. I'm also assuming that what I'm doing here is 
                            // expensive 

#if RESIZE
                            using (var resizedBitmap = await ResizeVideoFrame(videoFrame, IMAGE_SIZE, IMAGE_SIZE))
                            using (var resizedFrame = VideoFrame.CreateWithSoftwareBitmap(resizedBitmap))
                            {
                                this.inputData.data = resizedFrame;
#else       
                                this.inputData.data = videoFrame;
#endif // RESIZE

                                var evalOutput = await this.learningModel.EvaluateAsync(this.inputData);

                                await this.ProcessOutputAsync(evalOutput);

#if RESIZE
                            }
#endif // RESIZE
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref this.processingFlag, 0);
                }
            }
        }
        string BuildOutputString(DacshundModelOutput evalOutput, string key)
        {
            var result = "no";

            if (evalOutput.loss[key] > 0.25f)
            {
                result = $"{evalOutput.loss[key]:N2}";
            }
            return (result);
        }
        async Task ProcessOutputAsync(DacshundModelOutput evalOutput)
        {
            string category = evalOutput.classLabel.FirstOrDefault() ?? "none";
            string dog = $"{BuildOutputString(evalOutput, "dog")}";
            string pony = $"{BuildOutputString(evalOutput, "pony")}";
            string dacshund = $"{BuildOutputString(evalOutput, "daschund")}";

            await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    this.Dog = dog;
                    this.Pony = pony;
                    this.Dacshund = dacshund;
                    this.Category = category;
                }
            );
        }

        /// <summary>
        /// This is horrible - I am trying to resize a VideoFrame and I haven't yet
        /// found a good way to do it so this function goes through a tonne of
        /// stuff to try and resize it but it's not pleasant at all.
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        async static Task<SoftwareBitmap> ResizeVideoFrame(VideoFrame frame, int width, int height)
        {
            SoftwareBitmap bitmapFromFrame = null;
            bool ownsFrame = false;

            if (frame.Direct3DSurface != null)
            {
                bitmapFromFrame = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                    frame.Direct3DSurface,
                    BitmapAlphaMode.Ignore);

                ownsFrame = true;
            }
            else if (frame.SoftwareBitmap != null)
            {
                bitmapFromFrame = frame.SoftwareBitmap;
            }

            // We now need it in a pixel format that an encoder is happy with
            var encoderBitmap = SoftwareBitmap.Convert(
                bitmapFromFrame, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            if (ownsFrame)
            {
                bitmapFromFrame.Dispose();
            }

            // We now need an encoder, should we keep creating it?
            var memoryStream = new MemoryStream();

            var encoder = await BitmapEncoder.CreateAsync(
                BitmapEncoder.JpegEncoderId, memoryStream.AsRandomAccessStream());

            encoder.SetSoftwareBitmap(encoderBitmap);
            encoder.BitmapTransform.ScaledWidth = (uint)width;
            encoder.BitmapTransform.ScaledHeight = (uint)height;

            await encoder.FlushAsync();

            var decoder = await BitmapDecoder.CreateAsync(memoryStream.AsRandomAccessStream());

            var resizedBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            memoryStream.Dispose();

            encoderBitmap.Dispose();

            return (resizedBitmap);
        }
        void SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            storage = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        DacshundModelInput inputData;
        int processingFlag;
        MediaFrameReader frameReader;
        MediaCapture mediaCapture;
        DacshundModel learningModel;

        static readonly int IMAGE_SIZE = 227;
    }
}
