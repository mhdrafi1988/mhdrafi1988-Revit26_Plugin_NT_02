using BatchDwgFamilyLinker.ViewModels;

namespace BatchDwgFamilyLinker.Logging
{
    public static class BatchLogger
    {
        public static void Info(BatchLinkViewModel vm, string msg)
            => vm.AppendLog($"?? {msg}");

        public static void Warn(BatchLinkViewModel vm, string msg)
            => vm.AppendLog($"?? {msg}");

        public static void Error(BatchLinkViewModel vm, string msg)
            => vm.AppendLog($"?? {msg}");
    }
}
