using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_WINMD_SUPPORT
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using Windows.Storage;
using dachshunds.model;
using System.Diagnostics;
using System.Threading;
#endif // ENABLE_WINMD_SUPPORT

public class MainScript : MonoBehaviour
{
    public TextMesh textDisplay;

#if ENABLE_WINMD_SUPPORT
    public MainScript ()
	{
        this.inputData = new DachshundModelInput();
        this.timer = new Stopwatch();
	}
    async void Start()
    {
        await this.LoadModelAsync();

        var device = await this.GetFirstBackPanelVideoCaptureAsync();

        if (device != null)
        {
            await this.CreateMediaCaptureAsync(device);

            await this.CreateMediaFrameReaderAsync();
            await this.frameReader.StartAsync();
        }
    }    
    async Task LoadModelAsync()
    {
        // Get the bits from Unity's resource system :-S
        var modelBits = Resources.Load(DACHSHUND_MODEL_NAME) as TextAsset;

        this.learningModel = await DachshundModel.CreateDachshundModel(
            modelBits.bytes);
    }
    async Task<DeviceInformation> GetFirstBackPanelVideoCaptureAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(
            DeviceClass.VideoCapture);

        var device = devices.FirstOrDefault(
            d => d.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Back);

        return (device);
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

                        this.timer.Start();
                        var evalOutput = await this.learningModel.EvaluateAsync(this.inputData);
                        this.timer.Stop();
                        this.frameCount++;

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
    string BuildOutputString(DachshundModelOutput evalOutput, string key)
    {
        var result = "no";

        if (evalOutput.loss[key] > 0.25f)
        {
            result = $"{evalOutput.loss[key]:N2}";
        }
        return (result);
    }
    async Task ProcessOutputAsync(DachshundModelOutput evalOutput)
    {
        string category = evalOutput.classLabel.FirstOrDefault() ?? "none";
        string dog = $"{BuildOutputString(evalOutput, "dog")}";
        string pony = $"{BuildOutputString(evalOutput, "pony")}";

        // NB: Spelling mistake is built into model!
        string dachshund = $"{BuildOutputString(evalOutput, "daschund")}";
        string averageFrameDuration =
            this.frameCount == 0 ? "n/a" : $"{(this.timer.ElapsedMilliseconds / this.frameCount):N0}";

        UnityEngine.WSA.Application.InvokeOnAppThread(
            () =>
            {
                this.textDisplay.text = 
                    $"dachshund {dachshund} dog {dog} pony {pony}\navg time {averageFrameDuration}";
            },
            false
        );
    }
    DachshundModelInput inputData;
    int processingFlag;
    MediaFrameReader frameReader;
    MediaCapture mediaCapture;
    DachshundModel learningModel;
    Stopwatch timer;
    int frameCount;
    static readonly string DACHSHUND_MODEL_NAME = "dachshunds"; // .bytes file in Unity

#endif // ENABLE_WINMD_SUPPORT
}
