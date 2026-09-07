// The skeleton. Everything it can do today is prove that the two packages
// restore, that the DirectML provider is registered in this process, and that
// the solution still builds with warnings as errors.
//
// That is deliberate: the next commit is the whole prompt half of the tool,
// which needs neither a GPU nor a model file, and the one after that is the
// `inspect` command that reads the real ONNX graph instead of trusting anybody's
// memory of what its tensors are called.
using Microsoft.ML.OnnxRuntime;

Console.WriteLine("tendero seed images");
Console.WriteLine($"ONNX Runtime providers: {string.Join(", ", OrtEnv.Instance().GetAvailableProviders())}");

return 0;
