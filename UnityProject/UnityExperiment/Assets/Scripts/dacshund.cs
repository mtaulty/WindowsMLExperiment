#if ENABLE_WINMD_SUPPORT
namespace dachshunds.model
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices.WindowsRuntime;
    using System.Threading.Tasks;

    using Windows.AI.MachineLearning.Preview;
    using Windows.Media;
    using Windows.Storage;
    using Windows.Storage.Streams;

    // MIKET: I renamed the auto generated long number class names to be 'Daschund'
    // to make it easier for me as a human to deal with them :-)
    public sealed class DachshundModelInput
    {
        public VideoFrame data { get; set; }
    }

    public sealed class DachshundModelOutput
    {
        public IList<string> classLabel { get; set; }
        public IDictionary<string, float> loss { get; set; }

        public DachshundModelOutput()
        {
            this.classLabel = new List<string>();
            this.loss = new Dictionary<string, float>();

            // MIKET: I added these 3 lines of code here after spending *quite some time* :-)
            // Trying to debug why I was getting a binding excption at the point in the
            // code below where the call to LearningModelBindingPreview.Bind is called
            // with the parameters ("loss", output.loss) where output.loss would be
            // an empty Dictionary<string,float>.
            //
            // The exception would be 
            // "The binding is incomplete or does not match the input/output description. (Exception from HRESULT: 0x88900002)"
            // And I couldn't find symbols for Windows.AI.MachineLearning.Preview to debug it.
            // So...this could be wrong but it works for me and the 3 values here correspond
            // to the 3 classifications that my classifier produces.
            //
            this.loss.Add("daschund", float.NaN);
            this.loss.Add("dog", float.NaN);
            this.loss.Add("pony", float.NaN);
        }
    }

    public sealed class DachshundModel
    {
        private LearningModelPreview learningModel;

        public static async Task<DachshundModel> CreateDachshundModel(byte[] bits)
        {
            // Note - there is a method on LearningModelPreview which seems to
            // load from a stream but I got a 'not implemented' exception and
            // hence using a temporary file.
            IStorageFile file = null;
            var fileName = "model.bin";

            try
            {
                file = await ApplicationData.Current.TemporaryFolder.GetFileAsync(
                    fileName);
            }
            catch (FileNotFoundException)
            {
            }
            if (file == null)
            {
                file = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(
                    fileName);

                await FileIO.WriteBytesAsync(file, bits);
            }

            var model = await DachshundModel.CreateDachshundModel((StorageFile)file);

            return (model);
        }
        public static async Task<DachshundModel> CreateDachshundModel(StorageFile file)
        {
            LearningModelPreview learningModel = await LearningModelPreview.LoadModelFromStorageFileAsync(file);
            DachshundModel model = new DachshundModel();
            model.learningModel = learningModel;
            return model;
        }
        public async Task<DachshundModelOutput> EvaluateAsync(DachshundModelInput input) {
            DachshundModelOutput output = new DachshundModelOutput();
            LearningModelBindingPreview binding = new LearningModelBindingPreview(learningModel);
            binding.Bind("data", input.data);
            binding.Bind("classLabel", output.classLabel);

            // MIKET: this generated line caused me trouble. See MIKET comment above.
            binding.Bind("loss", output.loss);

            LearningModelEvaluationResultPreview evalResult = await learningModel.EvaluateAsync(binding, string.Empty);
            return output;
        }
    }
}
#endif // ENABLE_WINMD_SUPPORT
