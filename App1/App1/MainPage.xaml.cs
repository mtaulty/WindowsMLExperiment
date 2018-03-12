namespace App1
{
    using daschunds.model;
    using System;
    using System.ComponentModel;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Windows.Devices.Enumeration;
    using Windows.Media.Capture;
    using Windows.Media.Capture.Frames;
    using Windows.Media.Devices;
    using Windows.Storage;
    using Windows.UI.Core;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;

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
        public string Pony
        {
            get => this.pony;
            set => this.SetProperty(ref this.pony, value);
        }
        public string Dacshund
        {
            get => this.daschund;
            set => this.SetProperty(ref this.daschund, value);
        }
        public string Category
        {
            get => this.category;
            set => this.SetProperty(ref this.category, value);
        }
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
                            // odd size but I'm assuming the underlying pieces do that work
                            // for me.
                            // If you've read the blog post, I took out the conditional
                            // code which attempted to resize the frame as it seemed
                            // unnecessary and confused the issue!
                            this.inputData.data = videoFrame;

                            var evalOutput = await this.learningModel.EvaluateAsync(this.inputData);

                            await this.ProcessOutputAsync(evalOutput);
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

        string category;
        string daschund;
        string dog;
        string pony;
    }
}
