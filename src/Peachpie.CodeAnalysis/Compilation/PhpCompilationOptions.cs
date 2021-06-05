using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.PooledObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Immutable;
using Roslyn.Utilities;

namespace Pchp.CodeAnalysis
{
    /// <summary>
    /// Options for getting type information from correspodning PHPDoc comments.
    /// </summary>
    [Flags]
    public enum PhpDocTypes
    {
        None = 0,

        /// <summary>
        /// Fields type will be declared according to PHPDoc @var tag.
        /// </summary>
        FieldTypes = 1,

        /// <summary>
        /// Parameter type will be declared according to PHPDoc @param tag.
        /// </summary>
        ParameterTypes = 2,

        /// <summary>
        /// Method return type will be declared according to PHPDoc @return tag.
        /// </summary>
        ReturnTypes = 4,

        /// <summary>
        /// Declare all additional type information from PHPDoc.
        /// </summary>
        All = FieldTypes | ParameterTypes | ReturnTypes,
    }

    /// <summary>
    /// Options for enabling experimental optimization methods.
    /// </summary>
    [Flags]
    public enum ExperimentalOptimization
    {
        None = 0,

        /// <summary>
        /// When <see cref="PhpDocTypes.ParameterTypes"/> or <see cref="PhpDocTypes.ReturnTypes"/> are used,
        /// this switch causes them to be applied only on global functions, not on methods.
        /// </summary>
        PhpDocForceOnlyFunctions = 1 << 0,

        #region Overload creation

        /// <summary>
        /// Generates a type-specific overload for each routine with parameter types specified in PhpDocs.
        /// </summary>
        PhpDocOverloads = 1 << 1,

        /// <summary>
        /// Generates a type-specific overload for each routine where the parameter types are inferred from the
        /// types of the argument the routine is called with.
        /// </summary>
        CallSiteOverloads = 1 << 2,

        /// <summary>
        /// Generates a type-specific overload for each routine where the parameter types are inferred from their
        /// usages inside the routine.
        /// </summary>
        UsageOverloads = 1 << 3,

        /// <summary>
        /// Generates a type-specific overload for each routine where the parameter types are based on the optimization
        /// opportunities inside the routine while being called with these types from the outside.
        /// </summary>
        TargetedOverloads = 1 << 4,

        #endregion

        #region Call sites

        /// <summary>
        /// Attempt to call the specialized overloads as much as possible using dynamic call sites.
        /// </summary>
        DynamicCallSites = 1 << 8,

        /// <summary>
        /// Attempts to call the specialized overloads as much as possible by explicit type checking at call sites.
        /// </summary>
        BranchedCallSites = 1 << 9,

        #endregion

        #region Specialized parameter types

        SpecializeString = 1 << 12,
        SpecializePhpString = 1 << 13,
        SpecializeNumbers = 1 << 14,
        SpecializePhpArray = 1 << 15,
        SpecializeObjects = 1 << 16,
        SpecializeNull = 1 << 17,
        SpecializeMiscellaneous = 1 << 18,

        SpecializeCommon = SpecializeString | SpecializePhpString | SpecializeNumbers | SpecializePhpArray | SpecializeObjects,

        SpecializeAll = SpecializeCommon | SpecializeNull | SpecializeMiscellaneous,

        #endregion

        /// <summary>
        /// Disable casting <c>PhpString</c> to parameters specialized to <see cref="string"/>.
        /// </summary>
        DisableStringParameterCasting = 1 << 20,

        /// <summary>
        /// Allow only non-null reference type arguments for specialized parameters.
        /// </summary>
        ForceSpecializedParametersNotNull = 1 << 21,

        /// <summary>
        /// Prevent removing not null checks in parameters specialized to be distinct from null.
        /// </summary>
        RetainSpecializedParametersNotNullCheck = 1 << 22,

        /// <summary>
        /// Allow to emit argument specialization check of <c>PhpNumber</c> from <c>PhpValue</c>.
        /// </summary>
        AllowPhpNumberRuntimeSpecialization = 1 << 23,

        /// <summary>
        /// Enable to produce procedure specializations even when there are already existing ones from previous specialization rounds.
        /// </summary>
        EnableIncrementalSpecialization = 1 << 24,

        /// <summary>
        /// Produce an extra file with the list of all the specializations.
        /// </summary>
        RecordSpecializationList = 1 << 27,

        /// <summary>
        /// Produce an extra file with the time the whole compilation took.
        /// </summary>
        RecordCompilationTime = 1 << 28,

        /// <summary>
        /// Use <c>RuntimeTracing</c> to trace all the source routine calls.
        /// </summary>
        RoutineCallTracing = 1 << 29,

        /// <summary>
        /// Use <c>RuntimeTracing</c> to trace all the parameters source routines are called with.
        /// </summary>
        /// <remarks>
        /// Must be set in combination with <see cref="RoutineCallTracing"/>.
        /// </remarks>
        RoutineCallParameterTracing = 1 << 30,

        /// <summary>
        /// Use <c>RuntimeCounters</c> to count invocations of various routine types.
        /// </summary>
        RoutineCallCounting = 1 << 31,

        // TODO: These are for backward compatibility with wpdotnet-sdk fork, remove them when not needed anymore
        #region Common combinations

        PhpDocOverloadsStatic = PhpDocOverloads | SpecializeAll,
        PhpDocOverloadsDynamic = PhpDocOverloads | SpecializeAll | DynamicCallSites,
        PhpDocOverloadsBranch = PhpDocOverloads | SpecializeAll | BranchedCallSites,

        CallSiteOverloadsStatic = CallSiteOverloads | SpecializeAll,
        CallSiteOverloadsBranch = CallSiteOverloads | SpecializeAll | BranchedCallSites,

        UsageOverloadsStatic = UsageOverloads | SpecializeAll,
        UsageOverloadsBranch = UsageOverloads | SpecializeAll | BranchedCallSites,

        TargetedOverloadsStatic = TargetedOverloads | SpecializeAll,

        #endregion
    }

    public static class ExperimentalOptimizationExtensions
    {
        public static bool HasPhpDocOverloads(this ExperimentalOptimization optimization) =>
            (optimization & ExperimentalOptimization.PhpDocOverloads) != 0;

        public static bool HasCallSiteOverloads(this ExperimentalOptimization optimization) =>
            (optimization & ExperimentalOptimization.CallSiteOverloads) != 0;

        public static bool HasUsageOverloads(this ExperimentalOptimization optimization) =>
            (optimization & ExperimentalOptimization.UsageOverloads) != 0;

        public static bool HasStaticCallSites(this ExperimentalOptimization optimization) =>
            (optimization & (ExperimentalOptimization.DynamicCallSites | ExperimentalOptimization.BranchedCallSites )) == 0;

        public static bool HasBranchedCallSites(this ExperimentalOptimization optimization) =>
            (optimization & ExperimentalOptimization.BranchedCallSites) != 0;
    }

    /// <summary>
    /// Represents various options that affect compilation, such as 
    /// whether to emit an executable or a library, whether to optimize
    /// generated code, and so on.
    /// </summary>
    public sealed class PhpCompilationOptions : CompilationOptions, IEquatable<PhpCompilationOptions>
    {
        /// <summary>
        /// Compilation root directory.
        /// All script paths will be emitted relatively to this path.
        /// </summary>
        public string BaseDirectory { get; private set; }

        /// <summary>
        /// A path where all the compiled scripts will be moved virtually.
        /// File "a/index.php" will be compiled into "<see cref="SubDirectory"/>/a/index.php"
        /// </summary>
        public string SubDirectory { get; private set; }

        /// <summary>
        /// Compilation root directory.
        /// All script paths will be emitted relatively to this path.
        /// </summary>
        public string SdkDirectory { get; private set; }

        /// <summary>
        /// What framework is the compiled assembly supposed to run on,
        /// e.g. <c>.NETCoreApp,Version=v3.1</c>.
        /// </summary>
        public string TargetFramework { get; private set; }

        /// <summary>
        /// Options for getting type information from correspodning PHPDoc comments.
        /// </summary>
        public PhpDocTypes PhpDocTypes { get; private set; }

        /// <summary>
        /// Whether to generate an embedded resource containing additional information about the source symbols.
        /// Used by runtime for reflection.
        /// Default is <c>true</c>.
        /// </summary>
        public bool EmbedSourceMetadata { get; private set; }

        /// <summary>
        /// Source language options.
        /// </summary>
        public PhpParseOptions ParseOptions { get; private set; }

        /// <summary>
        /// The compilation language version.
        /// Gets <see cref="PhpParseOptions.LanguageVersion"/> or default language version if not specified.
        /// </summary>
        public Version LanguageVersion => ParseOptions?.LanguageVersion ?? PhpSyntaxTree.DefaultLanguageVersion;

        /// <summary>
        /// Options diagnostics.
        /// </summary>
        public ImmutableArray<Diagnostic> Diagnostics { get; private set; }

        /// <summary>
        /// The file version string.
        /// </summary>
        public string VersionString { get; private set; }

        /// <summary>
        /// List of observer instances.
        /// </summary>
        public ImmutableArray<IObserver<object>> EventSources { get; internal set; }

        /// <summary>
        /// The compilation optimization level.
        /// </summary>
        public new PhpOptimizationLevel OptimizationLevel { get; internal set; }

        /// <summary>
        /// Set of compile-time defined constants.
        /// </summary>
        public ImmutableDictionary<string, string> Defines { get; internal set; }

        /// <summary>
        /// Set of relative file names from which class map will be generated.
        /// Contained types will be marked as autoloaded.
        /// </summary>
        public ISet<string> Autoload_ClassMapFiles { get; internal set; }

        /// <summary>
        /// Set of relative file names to be marked as autoloaded.
        /// </summary>
        public ISet<string> Autoload_Files { get; internal set; }

        /// <summary>
        /// Collection of PSR-4 autoload rules.
        /// Matching types (classes, traits and interfaces) will be marked as autoloaded.
        /// </summary>
        public IReadOnlyCollection<(string prefix, string path)> Autoload_PSR4 { get; internal set; }

        /// <summary>
        /// Global Nullable context options.
        /// </summary>
        public override NullableContextOptions NullableContextOptions { get; protected set; }

        ///// <summary>
        ///// Flags applied to the top-level binder created for each syntax tree in the compilation 
        ///// as well as for the binder of global imports.
        ///// </summary>
        //internal BinderFlags TopLevelBinderFlags { get; private set; }

        /// <summary>
        /// Whether to generate a set of relations for the Souffle Datalog engine.
        /// </summary>
        public bool ExportSouffleRelations { get; private set; }

        /// <summary>
        /// Whether to enable a certain experimental optimization in the compiler.
        /// </summary>
        public ExperimentalOptimization ExperimentalOptimization { get; private set; }

        // Defaults correspond to the compiler's defaults or indicate that the user did not specify when that is significant.
        // That's significant when one option depends on another's setting. SubsystemVersion depends on Platform and Target.
        public PhpCompilationOptions(
            OutputKind outputKind,
            string baseDirectory,
            string sdkDirectory,
            string subDirectory = null,
            string targetFramework = null,
            bool reportSuppressedDiagnostics = false,
            string moduleName = null,
            string mainTypeName = null,
            string scriptClassName = null,
            string versionString = null,
            PhpOptimizationLevel optimizationLevel = PhpOptimizationLevel.Debug,
            bool checkOverflow = false,
            string cryptoKeyContainer = null,
            string cryptoKeyFile = null,
            ImmutableArray<byte> cryptoPublicKey = default,
            bool? delaySign = null,
            Platform platform = Platform.AnyCpu,
            ReportDiagnostic generalDiagnosticOption = ReportDiagnostic.Default,
            int warningLevel = 4,
            IEnumerable<KeyValuePair<string, ReportDiagnostic>> specificDiagnosticOptions = null,
            bool concurrentBuild = true,
            bool deterministic = false,
            DateTime currentLocalTime = default(DateTime),
            XmlReferenceResolver xmlReferenceResolver = null,
            SourceReferenceResolver sourceReferenceResolver = null,
            MetadataReferenceResolver metadataReferenceResolver = null,
            AssemblyIdentityComparer assemblyIdentityComparer = null,
            StrongNameProvider strongNameProvider = null,
            bool publicSign = false,
            PhpDocTypes phpdocTypes = PhpDocTypes.None,
            bool embedSourceMetadata = true,
            ImmutableArray<Diagnostic> diagnostics = default,
            PhpParseOptions parseOptions = null,
            ImmutableDictionary<string, string> defines = default,
            bool referencesSupersedeLowerVersions = false,
            NullableContextOptions nullableContextOptions = NullableContextOptions.Disable,
            bool exportSouffleRelations = false,
            ExperimentalOptimization experimentalOptimization = ExperimentalOptimization.None)
            : this(outputKind, baseDirectory, sdkDirectory, subDirectory, targetFramework,
                   reportSuppressedDiagnostics, moduleName, mainTypeName, scriptClassName,
                   versionString,
                   optimizationLevel, checkOverflow,
                   cryptoKeyContainer, cryptoKeyFile, cryptoPublicKey, delaySign, platform,
                   generalDiagnosticOption, warningLevel,
                   specificDiagnosticOptions, concurrentBuild, deterministic,
                   debugPlusMode: false,
                   currentLocalTime: currentLocalTime,
                   xmlReferenceResolver: xmlReferenceResolver,
                   sourceReferenceResolver: sourceReferenceResolver,
                   metadataReferenceResolver: metadataReferenceResolver,
                   assemblyIdentityComparer: assemblyIdentityComparer,
                   strongNameProvider: strongNameProvider,
                   metadataImportOptions: MetadataImportOptions.Public,
                   publicSign: publicSign,
                   phpdocTypes: phpdocTypes,
                   embedSourceMetadata: embedSourceMetadata,
                   diagnostics: diagnostics,
                   defines: defines,
                   parseOptions: parseOptions,
                   referencesSupersedeLowerVersions: referencesSupersedeLowerVersions,
                   nullableContextOptions: nullableContextOptions,
                   exportSouffleRelations: exportSouffleRelations,
                   experimentalOptimization: experimentalOptimization)
        {
        }

        // Expects correct arguments.
        internal PhpCompilationOptions(
            OutputKind outputKind,
            string baseDirectory,
            string sdkDirectory,
            string subDirectory,
            string targetFramework,
            bool reportSuppressedDiagnostics,
            string moduleName,
            string mainTypeName,
            string scriptClassName,
            string versionString,
            PhpOptimizationLevel optimizationLevel,
            bool checkOverflow,
            string cryptoKeyContainer,
            string cryptoKeyFile,
            ImmutableArray<byte> cryptoPublicKey,
            bool? delaySign,
            Platform platform,
            ReportDiagnostic generalDiagnosticOption,
            int warningLevel,
            IEnumerable<KeyValuePair<string, ReportDiagnostic>> specificDiagnosticOptions,
            bool concurrentBuild,
            bool deterministic,
            DateTime currentLocalTime,
            bool debugPlusMode,
            XmlReferenceResolver xmlReferenceResolver,
            SourceReferenceResolver sourceReferenceResolver,
            MetadataReferenceResolver metadataReferenceResolver,
            AssemblyIdentityComparer assemblyIdentityComparer,
            StrongNameProvider strongNameProvider,
            MetadataImportOptions metadataImportOptions,
            bool publicSign,
            PhpDocTypes phpdocTypes,
            bool embedSourceMetadata,
            ImmutableArray<Diagnostic> diagnostics,
            PhpParseOptions parseOptions,
            ImmutableDictionary<string, string> defines,
            bool referencesSupersedeLowerVersions,
            NullableContextOptions nullableContextOptions,
            bool exportSouffleRelations,
            ExperimentalOptimization experimentalOptimization)
            : base(outputKind, reportSuppressedDiagnostics, moduleName, mainTypeName, scriptClassName,
                   cryptoKeyContainer, cryptoKeyFile, cryptoPublicKey, delaySign, publicSign, optimizationLevel.AsOptimizationLevel(), checkOverflow,
                   platform, generalDiagnosticOption, warningLevel, specificDiagnosticOptions.ToImmutableDictionaryOrEmpty(),
                   concurrentBuild, deterministic, currentLocalTime, debugPlusMode, xmlReferenceResolver,
                   sourceReferenceResolver, metadataReferenceResolver, assemblyIdentityComparer,
                   strongNameProvider, metadataImportOptions, referencesSupersedeLowerVersions)
        {
            this.BaseDirectory = baseDirectory;
            this.SdkDirectory = sdkDirectory;
            this.SubDirectory = subDirectory;
            this.TargetFramework = targetFramework;
            this.PhpDocTypes = phpdocTypes;
            this.EmbedSourceMetadata = embedSourceMetadata;
            this.ParseOptions = parseOptions;
            this.Diagnostics = diagnostics;
            this.VersionString = versionString;
            this.OptimizationLevel = optimizationLevel;
            this.Defines = defines;
            this.NullableContextOptions = nullableContextOptions;
            this.ExportSouffleRelations = exportSouffleRelations;
            this.ExperimentalOptimization = experimentalOptimization;
        }

        private PhpCompilationOptions(PhpCompilationOptions other) : this(
            outputKind: other.OutputKind,
            baseDirectory: other.BaseDirectory,
            sdkDirectory: other.SdkDirectory,
            subDirectory: other.SubDirectory,
            targetFramework: other.TargetFramework,
            moduleName: other.ModuleName,
            mainTypeName: other.MainTypeName,
            scriptClassName: other.ScriptClassName,
            versionString: other.VersionString,
            optimizationLevel: other.OptimizationLevel,
            checkOverflow: other.CheckOverflow,
            cryptoKeyContainer: other.CryptoKeyContainer,
            cryptoKeyFile: other.CryptoKeyFile,
            cryptoPublicKey: other.CryptoPublicKey,
            delaySign: other.DelaySign,
            platform: other.Platform,
            generalDiagnosticOption: other.GeneralDiagnosticOption,
            warningLevel: other.WarningLevel,
            specificDiagnosticOptions: other.SpecificDiagnosticOptions,
            concurrentBuild: other.ConcurrentBuild,
            deterministic: other.Deterministic,
            currentLocalTime: other.CurrentLocalTime,
            debugPlusMode: other.DebugPlusMode,
            xmlReferenceResolver: other.XmlReferenceResolver,
            sourceReferenceResolver: other.SourceReferenceResolver,
            metadataReferenceResolver: other.MetadataReferenceResolver,
            assemblyIdentityComparer: other.AssemblyIdentityComparer,
            strongNameProvider: other.StrongNameProvider,
            metadataImportOptions: other.MetadataImportOptions,
            reportSuppressedDiagnostics: other.ReportSuppressedDiagnostics,
            publicSign: other.PublicSign,
            phpdocTypes: other.PhpDocTypes,
            embedSourceMetadata: other.EmbedSourceMetadata,
            diagnostics: other.Diagnostics,
            parseOptions: other.ParseOptions,
            defines: other.Defines,
            referencesSupersedeLowerVersions: other.ReferencesSupersedeLowerVersions,
            nullableContextOptions: other.NullableContextOptions,
            exportSouffleRelations: other.ExportSouffleRelations,
            experimentalOptimization: other.ExperimentalOptimization)
        {
            EventSources = other.EventSources;
            Autoload_ClassMapFiles = other.Autoload_ClassMapFiles;
            Autoload_Files = other.Autoload_Files;
            Autoload_PSR4 = other.Autoload_PSR4;
        }

        public override string Language => Constants.PhpLanguageName;

        internal override ImmutableArray<string> GetImports() => ImmutableArray<string>.Empty; // Usings;

        public new PhpCompilationOptions WithOutputKind(OutputKind kind)
        {
            if (kind == this.OutputKind)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { OutputKind = kind };
        }

        public new PhpCompilationOptions WithModuleName(string moduleName)
        {
            if (moduleName == this.ModuleName)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { ModuleName = moduleName };
        }

        public new PhpCompilationOptions WithScriptClassName(string name)
        {
            if (name == this.ScriptClassName)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { ScriptClassName = name };
        }

        public new PhpCompilationOptions WithMainTypeName(string name)
        {
            if (name == this.MainTypeName)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { MainTypeName = name };
        }

        public new PhpCompilationOptions WithCryptoKeyContainer(string name)
        {
            if (name == this.CryptoKeyContainer)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { CryptoKeyContainer = name };
        }

        public new PhpCompilationOptions WithCryptoKeyFile(string path)
        {
            if (path == this.CryptoKeyFile)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { CryptoKeyFile = path };
        }

        public new PhpCompilationOptions WithCryptoPublicKey(ImmutableArray<byte> value)
        {
            if (value.IsDefault)
            {
                value = ImmutableArray<byte>.Empty;
            }

            if (value == this.CryptoPublicKey)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { CryptoPublicKey = value };
        }

        public new PhpCompilationOptions WithDelaySign(bool? value)
        {
            if (value == this.DelaySign)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { DelaySign = value };
        }

        public new PhpCompilationOptions WithOptimizationLevel(OptimizationLevel value)
        {
            if (value == base.OptimizationLevel)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { OptimizationLevel = value.AsPhpOptimizationLevel() };
        }

        public new PhpCompilationOptions WithOverflowChecks(bool enabled)
        {
            if (enabled == this.CheckOverflow)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { CheckOverflow = enabled };
        }

        public new PhpCompilationOptions WithPlatform(Platform platform)
        {
            if (this.Platform == platform)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { Platform = platform };
        }

        public new PhpCompilationOptions WithPublicSign(bool publicSign)
        {
            if (this.PublicSign == publicSign)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { PublicSign = publicSign };
        }

        public PhpCompilationOptions WithParseOptions(PhpParseOptions parseoptions)
        {
            if (ReferenceEquals(this.ParseOptions, parseoptions))
            {
                return this;
            }

            return new PhpCompilationOptions(this) { ParseOptions = parseoptions };
        }

        protected override CompilationOptions CommonWithGeneralDiagnosticOption(ReportDiagnostic value) => WithGeneralDiagnosticOption(value);

        protected override CompilationOptions CommonWithSpecificDiagnosticOptions(ImmutableDictionary<string, ReportDiagnostic> specificDiagnosticOptions) =>
            WithSpecificDiagnosticOptions(specificDiagnosticOptions);

        protected override CompilationOptions CommonWithSpecificDiagnosticOptions(IEnumerable<KeyValuePair<string, ReportDiagnostic>> specificDiagnosticOptions) =>
            WithSpecificDiagnosticOptions(specificDiagnosticOptions);

        protected override CompilationOptions CommonWithReportSuppressedDiagnostics(bool reportSuppressedDiagnostics) =>
            WithReportSuppressedDiagnostics(reportSuppressedDiagnostics);

        public new PhpCompilationOptions WithGeneralDiagnosticOption(ReportDiagnostic value)
        {
            if (this.GeneralDiagnosticOption == value)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { GeneralDiagnosticOption = value };
        }

        public new PhpCompilationOptions WithSpecificDiagnosticOptions(ImmutableDictionary<string, ReportDiagnostic> values)
        {
            if (values == null)
            {
                values = ImmutableDictionary<string, ReportDiagnostic>.Empty;
            }

            if (this.SpecificDiagnosticOptions == values)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { SpecificDiagnosticOptions = values };
        }

        public new PhpCompilationOptions WithSpecificDiagnosticOptions(IEnumerable<KeyValuePair<string, ReportDiagnostic>> values) =>
            new PhpCompilationOptions(this) { SpecificDiagnosticOptions = values.ToImmutableDictionaryOrEmpty() };

        public new PhpCompilationOptions WithReportSuppressedDiagnostics(bool reportSuppressedDiagnostics)
        {
            if (reportSuppressedDiagnostics == this.ReportSuppressedDiagnostics)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { ReportSuppressedDiagnostics = reportSuppressedDiagnostics };
        }

        public PhpCompilationOptions WithWarningLevel(int warningLevel)
        {
            if (warningLevel == this.WarningLevel)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { WarningLevel = warningLevel };
        }

        public new PhpCompilationOptions WithConcurrentBuild(bool concurrentBuild)
        {
            if (concurrentBuild == this.ConcurrentBuild)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { ConcurrentBuild = concurrentBuild };
        }

        public new PhpCompilationOptions WithDeterministic(bool deterministic)
        {
            if (deterministic == this.Deterministic)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { Deterministic = deterministic };
        }

        public PhpCompilationOptions WithDebugPlusMode(bool debugPlusMode)
        {
            if (debugPlusMode == this.DebugPlusMode)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { DebugPlusMode_internal_protected_set = debugPlusMode };
        }

        public PhpCompilationOptions WithDefines(ImmutableDictionary<string, string> defines)
        {
            if (this.Defines == defines)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { Defines = defines };
        }

        public new PhpCompilationOptions WithMetadataImportOptions(MetadataImportOptions value)
        {
            if (value == this.MetadataImportOptions)
            {
                return this;
            }

            return new PhpCompilationOptions(this) { MetadataImportOptions = value };
        }

        public new PhpCompilationOptions WithXmlReferenceResolver(XmlReferenceResolver resolver)
        {
            if (ReferenceEquals(resolver, this.XmlReferenceResolver))
            {
                return this;
            }

            return new PhpCompilationOptions(this) { XmlReferenceResolver = resolver };
        }

        public new PhpCompilationOptions WithSourceReferenceResolver(SourceReferenceResolver resolver)
        {
            if (ReferenceEquals(resolver, this.SourceReferenceResolver))
            {
                return this;
            }

            return new PhpCompilationOptions(this) { SourceReferenceResolver = resolver };
        }

        public new PhpCompilationOptions WithMetadataReferenceResolver(MetadataReferenceResolver resolver)
        {
            if (ReferenceEquals(resolver, this.MetadataReferenceResolver))
            {
                return this;
            }

            return new PhpCompilationOptions(this) { MetadataReferenceResolver = resolver };
        }

        public new PhpCompilationOptions WithAssemblyIdentityComparer(AssemblyIdentityComparer comparer)
        {
            comparer = comparer ?? AssemblyIdentityComparer.Default;

            if (ReferenceEquals(comparer, this.AssemblyIdentityComparer))
            {
                return this;
            }

            return new PhpCompilationOptions(this) { AssemblyIdentityComparer = comparer };
        }

        public new PhpCompilationOptions WithStrongNameProvider(StrongNameProvider provider)
        {
            if (ReferenceEquals(provider, this.StrongNameProvider))
            {
                return this;
            }

            return new PhpCompilationOptions(this) { StrongNameProvider = provider };
        }

        protected override CompilationOptions CommonWithDeterministic(bool deterministic) => WithDeterministic(deterministic);

        protected override CompilationOptions CommonWithOutputKind(OutputKind kind) => WithOutputKind(kind);

        protected override CompilationOptions CommonWithPlatform(Platform platform) => WithPlatform(platform);

        protected override CompilationOptions CommonWithPublicSign(bool publicSign) => WithPublicSign(publicSign);

        protected override CompilationOptions CommonWithOptimizationLevel(OptimizationLevel value) => WithOptimizationLevel(value);

        protected override CompilationOptions CommonWithAssemblyIdentityComparer(AssemblyIdentityComparer comparer) =>
            WithAssemblyIdentityComparer(comparer);

        protected override CompilationOptions CommonWithXmlReferenceResolver(XmlReferenceResolver resolver) =>
            WithXmlReferenceResolver(resolver);

        protected override CompilationOptions CommonWithSourceReferenceResolver(SourceReferenceResolver resolver) =>
            WithSourceReferenceResolver(resolver);

        protected override CompilationOptions CommonWithMetadataReferenceResolver(MetadataReferenceResolver resolver) =>
            WithMetadataReferenceResolver(resolver);

        protected override CompilationOptions CommonWithStrongNameProvider(StrongNameProvider provider) =>
            WithStrongNameProvider(provider);

        protected override CompilationOptions CommonWithConcurrentBuild(bool concurrent) =>
            WithConcurrentBuild(concurrent);

        protected override CompilationOptions CommonWithModuleName(string moduleName) =>
            WithModuleName(moduleName);

        protected override CompilationOptions CommonWithMainTypeName(string mainTypeName) =>
            WithMainTypeName(mainTypeName);

        protected override CompilationOptions CommonWithScriptClassName(string scriptClassName) =>
            WithScriptClassName(scriptClassName);

        protected override CompilationOptions CommonWithCryptoKeyContainer(string cryptoKeyContainer) =>
            WithCryptoKeyContainer(cryptoKeyContainer);

        protected override CompilationOptions CommonWithCryptoKeyFile(string cryptoKeyFile) =>
            WithCryptoKeyFile(cryptoKeyFile);

        protected override CompilationOptions CommonWithCryptoPublicKey(ImmutableArray<byte> cryptoPublicKey) =>
            WithCryptoPublicKey(cryptoPublicKey);

        protected override CompilationOptions CommonWithDelaySign(bool? delaySign) =>
            WithDelaySign(delaySign);

        protected override CompilationOptions CommonWithCheckOverflow(bool checkOverflow) =>
            WithOverflowChecks(checkOverflow);

        protected override CompilationOptions CommonWithMetadataImportOptions(MetadataImportOptions value) =>
            WithMetadataImportOptions(value);

        [Obsolete]
        protected override CompilationOptions CommonWithFeatures(ImmutableArray<string> features)
        {
            throw new NotImplementedException();
        }

        internal override void ValidateOptions(ArrayBuilder<Diagnostic> builder)
        {
            ////  /main & /target:{library|netmodule|winmdobj}
            //if (this.MainTypeName != null)
            //{
            //    if (this.OutputKind.IsValid() && !this.OutputKind.IsApplication())
            //    {
            //        builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_NoMainOnDLL));
            //    }

            //    if (!MainTypeName.IsValidClrTypeName())
            //    {
            //        builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_BadCompilationOptionValue, nameof(MainTypeName), MainTypeName));
            //    }
            //}

            //if (!Platform.IsValid())
            //{
            //    builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_BadPlatformType, Platform.ToString()));
            //}

            //if (ModuleName != null)
            //{
            //    Exception e = MetadataHelpers.CheckAssemblyOrModuleName(ModuleName, nameof(ModuleName));
            //    if (e != null)
            //    {
            //        builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_BadCompilationOption, e.Message));
            //    }
            //}

            //if (!OutputKind.IsValid())
            //{
            //    builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_BadCompilationOptionValue, nameof(OutputKind), OutputKind.ToString()));
            //}

            //if (!OptimizationLevel.IsValid())
            //{
            //    builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_BadCompilationOptionValue, nameof(OptimizationLevel), OptimizationLevel.ToString()));
            //}

            //if (ScriptClassName == null || !ScriptClassName.IsValidClrTypeName())
            //{
            //    builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_BadCompilationOptionValue, nameof(ScriptClassName), ScriptClassName ?? "null"));
            //}

            //if (WarningLevel < 0 || WarningLevel > 4)
            //{
            //    builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_BadCompilationOptionValue, nameof(WarningLevel), WarningLevel));
            //}

            //if (Usings != null && Usings.Any(u => !u.IsValidClrNamespaceName()))
            //{
            //    builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_BadCompilationOptionValue, nameof(Usings), Usings.Where(u => !u.IsValidClrNamespaceName()).First() ?? "null"));
            //}

            //if (Platform == Platform.AnyCpu32BitPreferred && OutputKind.IsValid() && !(OutputKind == OutputKind.ConsoleApplication || OutputKind == OutputKind.WindowsApplication || OutputKind == OutputKind.WindowsRuntimeApplication))
            //{
            //    builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_BadPrefer32OnLib));
            //}

            //// TODO: add check for 
            ////          (kind == 'arm' || kind == 'appcontainer' || kind == 'winmdobj') &&
            ////          (version >= "6.2")

            //if (!CryptoPublicKey.IsEmpty)
            //{
            //    if (CryptoKeyFile != null)
            //    {
            //        builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_MutuallyExclusiveOptions, nameof(CryptoPublicKey), nameof(CryptoKeyFile)));
            //    }

            //    if (CryptoKeyContainer != null)
            //    {
            //        builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_MutuallyExclusiveOptions, nameof(CryptoPublicKey), nameof(CryptoKeyContainer)));
            //    }
            //}

            //if (PublicSign && DelaySign == true)
            //{
            //    builder.Add(Diagnostic.Create(MessageProvider.Instance, (int)ErrorCode.ERR_MutuallyExclusiveOptions, nameof(PublicSign), nameof(DelaySign)));
            //}
        }

        public bool Equals(PhpCompilationOptions other)
        {
            if (object.ReferenceEquals(this, other))
            {
                return true;
            }

            if (!base.EqualsHelper(other))
            {
                return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return this.Equals(obj as PhpCompilationOptions);
        }

        public override int GetHashCode()
        {
            return base.GetHashCodeHelper();
        }

        internal override Diagnostic FilterDiagnostic(Diagnostic diagnostic)
        {
            // return PhpDiagnosticFilter.Filter(diagnostic, WarningLevel, GeneralDiagnosticOption, SpecificDiagnosticOptions);

            if (diagnostic == null)
            {
                return null;
            }

            ReportDiagnostic reportAction;

            if (SpecificDiagnosticOptions != null && SpecificDiagnosticOptions.TryGetValue(diagnostic.Id, out ReportDiagnostic d))
            {
                reportAction = d;
            }
            else
            {
                reportAction = ReportDiagnostic.Default;
            }

            return diagnostic.WithReportDiagnostic(reportAction);
        }
    }
}
