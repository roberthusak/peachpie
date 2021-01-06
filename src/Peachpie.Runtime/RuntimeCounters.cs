using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Pchp.Core
{
    public static class RuntimeCounters
    {
        private static int _branchedCallChecks = 0;
        private static int _branchedCallOriginalSelects = 0;
        private static int _branchedCallSpecializedSelects = 0;
        private static int _originalOverloadCalls = 0;
        private static int _specializedOverloadCalls = 0;

        public static int BranchedCallChecks => _branchedCallChecks;
        public static int BranchedCallOriginalSelects => _branchedCallOriginalSelects;
        public static int BranchedCallSpecializedSelects => _branchedCallSpecializedSelects;
        public static int OriginalOverloadCalls => _originalOverloadCalls;
        public static int SpecializedOverloadCalls => _specializedOverloadCalls;

        public static void MarkBranchedCallCheck() => Interlocked.Increment(ref _branchedCallChecks);
        public static void MarkBranchedCallOriginalSelect() => Interlocked.Increment(ref _branchedCallOriginalSelects);
        public static void MarkBranchedCallSpecializedSelect() => Interlocked.Increment(ref _branchedCallSpecializedSelects);
        public static void MarkOriginalOverloadCall() => Interlocked.Increment(ref _originalOverloadCalls);
        public static void MarkSpecializedOverloadCall() => Interlocked.Increment(ref _specializedOverloadCalls);
    }
}
