namespace daschunds.model
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Windows.AI.MachineLearning.Preview;
    using Windows.Media;
    using Windows.Storage;

    // MIKET: I renamed the auto generated long number class names to be 'Daschund'
    // to make it easier for me as a human to deal with them :-)
    public sealed class DacshundModelInput
    {
        public VideoFrame data { get; set; }
    }

    public sealed class DacshundModelOutput
    {
        public IList<string> classLabel { get; set; }
        public IDictionary<string, float> loss { get; set; }
        public DacshundModelOutput()
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

    public sealed class DacshundModel
    {
        private LearningModelPreview learningModel;
        public static async Task<DacshundModel> CreateDaschundModel(StorageFile file)
        {
            LearningModelPreview learningModel = await LearningModelPreview.LoadModelFromStorageFileAsync(file);
            DacshundModel model = new DacshundModel();
            model.learningModel = learningModel;
            return model;
        }
        public async Task<DacshundModelOutput> EvaluateAsync(DacshundModelInput input) {
            DacshundModelOutput output = new DacshundModelOutput();
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
