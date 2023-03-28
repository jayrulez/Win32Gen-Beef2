using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Win32Generator
{
    public class Options
    {
        [Option('i', "inputDir", Required = true, HelpText = "The path to the directory containing the JSON metadata files.")]
        public string InputDir { get; set; }

        [Option('o', "outputDir", Required = true, HelpText = "The path where the generated bindings are to be written.")]
        public string OutputDir { get; set; }
    }

    class Program
    {
        public static HashSet<string> NativeTypeDefsAndEnums = new HashSet<string>();

        private const string RootNamespace = "Win32";

        private static Dictionary<string, string> NativeTypes = new Dictionary<string, string>()
        {
            {"Int8", "int8" },
            {"UInt8", "uint8" },
            {"Int16", "int16" },
            {"UInt16", "uint16" },
            {"Int32", "int32" },
            {"UInt32", "uint32" },
            {"Int64", "int64" },
            {"UInt64", "uint64" },
            {"IntPtr", "int" },
            {"UIntPtr", "uint" },
            {"Byte", "uint8" },
            {"SByte", "int8" },
            {"Void", "void" },
            {"Single", "float" },
            {"Double", "double" },
            {"Boolean", "bool" },
            {"Char", "char16" },
            {"Guid", "Guid" },
        };

        private static List<string> ReservedWords = new List<string>()
        {
            "ref",
            "int",
            "out",
            "var",
            "scope",
            "params",
            "function",
            "internal",
            "defer",
            "delegate",
            "append",
            "where",
            "abstract",
            "repeat",
            "extension",
            "override",
            "stack",
            "base",
            "in",
            "as",
            "mut"
        };

        private static List<string> replaceCOMMethods = new List<string>()
        {
            "Equals",
            "GetFlags",
            "GetType",
            "ToString"
        };

        private static Dictionary<string, string> NativeTypedefs = new Dictionary<string, string>();

        private static Dictionary<string, string> FundamentalTypes = new Dictionary<string, string>();

        private static List<APIFile> APIFiles = new List<APIFile>();
        private static List<APIFile> ProcessedAPIFiles = new();

        private static Dictionary<string, List<JObject>> ProcessedStructsOrUnions = new Dictionary<string, List<JObject>>();

        public static void Main(string[] args)
        {
            CommandLine.Parser.Default.ParseArguments<Options>(args)
                .WithParsed(RunGenerator)
                .WithNotParsed(HandleParseError);

            Console.WriteLine("Done! Press any key to continue.");

            Console.ReadKey();
        }

        static void HandleParseError(IEnumerable<Error> errors)
        {
            foreach (var error in errors)
            {
                Console.WriteLine(error);
            }
        }

        static void RunGenerator(Options options)
        {
            if (!Directory.Exists(options.InputDir))
            {
                Console.WriteLine($"Input directory '{options.InputDir}' does not exist.");
                return;
            }

            if (!Directory.Exists(options.OutputDir))
            {
                Console.WriteLine($"Output directory '{options.OutputDir}' does not exist.");

                do
                {
                    Console.Write($"Create output directory '{options.OutputDir}'? Enter Y/N: ");

                    var response = Console.ReadKey();

                    if (response.KeyChar == 'n' || response.KeyChar == 'N')
                    {
                        return;
                    }
                    else if (response.KeyChar == 'y' || response.KeyChar == 'Y')
                    {
                        try
                        {
                            Directory.CreateDirectory(options.OutputDir!);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to create output directory '{options.OutputDir}': {ex.Message}");
                        }

                        break;
                    }
                    else
                    {
                        Console.WriteLine($"Invalid response '{response.KeyChar}'. Enter 'Y' or 'N'.");
                    }

                } while (true);
            }

            var files = Directory.GetFiles(options.InputDir, "*.json");

            foreach (var file in files)
            {
                var fileNameSegments = file
                    .Replace(".json", "")
                    .Replace(options.InputDir, "")
                    .Replace(Path.DirectorySeparatorChar.ToString(), "")
                    .Split('.');

                // data.help_json

                var dirParts = fileNameSegments[..(fileNameSegments.Length - 1)];
                var filePart = fileNameSegments[fileNameSegments.Length - 1];

                var dirString = string.Join(Path.DirectorySeparatorChar.ToString(), dirParts);
                var fileString = Path.Combine(dirString, filePart);

                var apiString = fileString.Replace(Path.DirectorySeparatorChar.ToString(), ".");

                var outputPath = Path.Combine(options.OutputDir, "Generated", dirString);

                var apiFile = new APIFile(filePart, apiString, file, outputPath);

                APIFiles.Add(apiFile);


                var inputContent = File.ReadAllText(apiFile.InputPath);

                apiFile.Content = JObject.Parse(inputContent);

                var occurences = new Regex(@"""Api"":""[a-zA-Z_0-9..]+""");

                MatchCollection matches = occurences.Matches(inputContent);

                foreach (var match in matches)
                {
                    var referencedApi = match.ToString()
                        .Replace("\"", "")
                        .Replace("Api:", "");

                    if (referencedApi != apiFile.Api)
                        apiFile.Dependencies.Add(referencedApi);
                }

                // Gather types and native typedefs and enums, Used to exclude them when modiflying COM signatures where return types need to be an out param
                foreach (JObject type in apiFile.Content["Types"].ToObject<JArray>())
                {
                    if (type["Kind"]?.ToString() == "Enum" || type["Kind"]?.ToString() == "NativeTypedef")
                    {
                        NativeTypeDefsAndEnums.Add(type["Name"].ToString());
                    }
                }
            }

            APIFiles.OrderBy(f => f.Dependencies.Count);

            ProcessAPIFiles(APIFiles);

            var extrasStringBuilder = new StringBuilder();

            //extrasStringBuilder.AppendLine($"namespace {RootNamespace}");
            //extrasStringBuilder.AppendLine("{");
            //AddTabs(1, ref extrasStringBuilder);
            //extrasStringBuilder.AppendLine($"public static");
            //AddTabs(1, ref extrasStringBuilder);
            //extrasStringBuilder.AppendLine("{");
            //{
            //    AddTabs(2, ref extrasStringBuilder);
            //    extrasStringBuilder.AppendLine("public const uint ANYSIZE_ARRAY = 1;");
            //    AddTabs(2, ref extrasStringBuilder);
            //    extrasStringBuilder.AppendLine("public const uint32 FALSE = 0;");
            //    AddTabs(2, ref extrasStringBuilder);
            //    extrasStringBuilder.AppendLine("public const uint32 TRUE = 1;");
            //}
            //AddTabs(1, ref extrasStringBuilder);
            //extrasStringBuilder.AppendLine("}");


            //extrasStringBuilder.AppendLine("}");
            //extrasStringBuilder.AppendLine();

            //extrasStringBuilder.AppendLine("namespace Win32.UI.Shell.PropertiesSystem");
            //extrasStringBuilder.AppendLine("{");
            //{
            //    AddTabs(1, ref extrasStringBuilder);
            //    extrasStringBuilder.AppendLine("extension PROPERTYKEY");
            //    AddTabs(1, ref extrasStringBuilder);
            //    extrasStringBuilder.AppendLine("{");
            //    {
            //        AddTabs(2, ref extrasStringBuilder);
            //        extrasStringBuilder.AppendLine("public this(System.Guid fmtid, uint32 pid)");

            //        AddTabs(2, ref extrasStringBuilder);
            //        extrasStringBuilder.AppendLine("{");
            //        {
            //            AddTabs(3, ref extrasStringBuilder);
            //            extrasStringBuilder.AppendLine("this.fmtid = fmtid;");
            //            AddTabs(3, ref extrasStringBuilder);
            //            extrasStringBuilder.AppendLine("this.pid = pid;");
            //        }
            //        AddTabs(2, ref extrasStringBuilder);
            //        extrasStringBuilder.AppendLine("}");
            //    }
            //    AddTabs(1, ref extrasStringBuilder);
            //    extrasStringBuilder.AppendLine("}");
            //}
            //extrasStringBuilder.AppendLine("}");

            //var win32Extras = $$"""
            //namespace Win32
            //{
            //	using Win32.Foundation;
            //	using Win32.System.Diagnostics.Debug;
            //	using System;

            //	public static
            //	{
            //		public const uint ANYSIZE_ARRAY = 1;
            //		public const uint32 FALSE = 0;
            //		public const uint32 TRUE = 1;

            //		public static bool SUCCEEDED(HRESULT hr)
            //		{
            //			return hr >= 0;
            //		}

            //		public static bool FAILED(HRESULT hr)
            //		{
            //			return hr < 0;
            //		}

            //		public static HRESULT HRESULT_FROM_WIN32(uint64 win32Error)
            //		{
            //			return (HRESULT)(win32Error) <= 0 ? (HRESULT)(win32Error) : (HRESULT)(((win32Error) & 0x0000FFFF) | ((uint32)FACILITY_CODE.FACILITY_WIN32 << 16) | 0x80000000);
            //		}

            //		public static mixin FOURCC(char8 ch0, char8 ch1, char8 ch2, char8 ch3)
            //		{
            //			((uint32)(uint8)(ch0) | ((uint32)(uint8)(ch1) << 8) | ((uint32)(uint8)(ch2) << 16) | ((uint32)(uint8)(ch3) << 24))
            //		}

            //		[Comptime(ConstEval = true)]
            //		public static uint32 FOURCC(String str)
            //		{
            //			Runtime.Assert(str.Length == 4);
            //			return (uint32)(uint8)(str[0]) | (uint32)(uint8)(str[1]) << 8 | (uint32)(uint8)(str[2]) << 16 | (uint32)(uint8)(str[3]) << 24;
            //		}
            //	}
            //}

            //namespace Win32.Foundation
            //{
            //	extension WIN32_ERROR
            //	{
            //		public static implicit operator uint64(Self self) => (uint64)self.Underlying;
            //	}
            //}

            //namespace Win32.Networking.WinSock
            //{
            //	public static
            //	{
            //		public const uint32 INADDR_ANY       = (.)0x00000000;
            //		public const uint32 ADDR_ANY         = INADDR_ANY;
            //		public const uint32 INADDR_BROADCAST = (.)0xffffffff;
            //	}
            //}

            //namespace Win32.UI.Shell.PropertiesSystem
            //{
            //	extension PROPERTYKEY
            //	{
            //		public this(System.Guid fmtid, uint32 pid)
            //		{
            //			this.fmtid = fmtid;
            //			this.pid = pid;
            //		}
            //	}
            //}

            //namespace Win32.Devices.Properties
            //{
            //	extension DEVPROPKEY
            //	{
            //		public this(System.Guid fmtid, uint32 pid)
            //		{
            //			this.fmtid = fmtid;
            //			this.pid = pid;
            //		}
            //	}
            //}
            //""";

            //extrasStringBuilder.Append(win32Extras);


            //File.WriteAllText(Path.Combine(options.OutputDir, "Support.bf"), extrasStringBuilder.ToString());
        }

        private static void ProcessAPIFiles(List<APIFile> apiFiles)
        {
            foreach (var apiFile in apiFiles)
            {
                if (!ProcessedAPIFiles.Contains(apiFile))
                {
                    ProcessAPIFile(apiFile);
                }
            }
        }

        private static void ProcessAPIFile(APIFile apiFile)
        {
            if (ProcessedAPIFiles.Contains(apiFile))
                return;

            var constants = apiFile.Content.GetValue("Constants")!.ToObject<JArray>();
            var types = apiFile.Content.GetValue("Types")!.ToObject<JArray>();
            var functions = apiFile.Content.GetValue("Functions")!.ToObject<JArray>();
            var unicodeAliases = apiFile.Content.GetValue("UnicodeAliases")!.ToObject<JArray>();

            var typesBuilder = new TypesBuilder();
            var constantsContent = new StringBuilder();
            var functionsContent = new StringBuilder();

            HashSet<string> structOrUnionReferencedApis = new HashSet<string>();

            _ProcessTypes(types, null, typesBuilder, 0, apiFile.Api, ref structOrUnionReferencedApis);
            _ProcessConstants(constants, ref constantsContent, 0);
            _ProcessFunctions(functions, unicodeAliases, ref functionsContent, 0);

            var outputContent = new StringBuilder();

            if (apiFile.Dependencies.Count > 0)
            {
                foreach (var dedepndencyApi in apiFile.Dependencies)
                {
                    outputContent.AppendLine($"using {RootNamespace}.{dedepndencyApi};");
                }
            }

            outputContent.AppendLine($"using System;");
            foreach (var referencedApi in structOrUnionReferencedApis)
            {
                outputContent.AppendLine($"using {referencedApi};");
            }
            outputContent.AppendLine($"");

            var fileName = Path.GetFileName(apiFile.InputPath);
            var @namespace = $"{RootNamespace}.{apiFile.Api}";

            outputContent.AppendLine($"namespace {@namespace};");

            // Write out constants
            if (constantsContent.Length > 0)
            {
                outputContent.AppendLine();
                outputContent.AppendLine("#region Constants");
                outputContent.AppendLine("public static");
                outputContent.AppendLine("{");

                if (apiFile.Api.Contains("Globalization"))
                {
                    int v = 1;
                }

                outputContent.Append(constantsContent);

                outputContent.AppendLine("}");
                outputContent.AppendLine("#endregion");
            }

            // native typedefs

            if (typesBuilder.NativeTypedefs.Length > 0)
            {
                outputContent.AppendLine();
                outputContent.AppendLine("#region TypeDefs");
                outputContent.Append(typesBuilder.NativeTypedefs);
                outputContent.AppendLine("#endregion");
                outputContent.AppendLine();
            }

            // enums
            if (typesBuilder.Enums.Length > 0)
            {
                outputContent.AppendLine();
                outputContent.AppendLine("#region Enums");
                outputContent.Append(typesBuilder.Enums);
                outputContent.AppendLine("#endregion");
                outputContent.AppendLine();
            }

            // function pointers
            if (typesBuilder.FunctionPointers.Length > 0)
            {
                outputContent.AppendLine("#region Function Pointers");
                outputContent.Append(typesBuilder.FunctionPointers);
                outputContent.AppendLine("#endregion");
            }

            // structs and unions
            if (typesBuilder.StructsOrUnions.Length > 0)
            {
                outputContent.AppendLine();
                outputContent.AppendLine("#region Structs");
                outputContent.Append(typesBuilder.StructsOrUnions);
                outputContent.AppendLine("#endregion");
            }

            // com class ids
            if (typesBuilder.ComClassIDs.Length > 0)
            {
                outputContent.AppendLine();
                outputContent.AppendLine("#region COM Class IDs");
                outputContent.AppendLine("public static");
                outputContent.AppendLine("{");
                outputContent.Append(typesBuilder.ComClassIDs);
                outputContent.AppendLine("}");
                outputContent.AppendLine("#endregion");
            }

            // com
            if (typesBuilder.Com.Length > 0)
            {
                outputContent.AppendLine();
                outputContent.AppendLine("#region COM Types");
                outputContent.Append(typesBuilder.Com);
                outputContent.AppendLine("#endregion");
            }


            // Frite out functions
            if (functionsContent.Length > 0)
            {
                outputContent.AppendLine();
                outputContent.AppendLine("#region Functions");

                outputContent.AppendLine("public static");
                outputContent.AppendLine("{");
                outputContent.Append(functionsContent.ToString());
                outputContent.AppendLine("}");
                outputContent.AppendLine("#endregion");
            }

            if (!Directory.Exists(apiFile.OutputPath))
                Directory.CreateDirectory(apiFile.OutputPath);

            var outputFilePath = Path.Join(apiFile.OutputPath, apiFile.Name + ".bf");

            File.WriteAllText(outputFilePath, outputContent.ToString());

            //Console.WriteLine($"Generated '{outputFilePath}' from '{apiFile.InputPath}'.");

            ProcessedAPIFiles.Add(apiFile);
            apiFile.Content = null;
        }

        private static void _ProcessFunctions(JArray functions, JArray unicodeAliases, ref StringBuilder outputContent, int indentLevel)
        {
            List<String> unicodeAliasNames = new List<string>();
            if (unicodeAliases.Count > 0)
            {
                foreach (var ua in unicodeAliases)
                {
                    unicodeAliasNames.Add(ua.ToString());
                }
            }

            foreach (var function in functions)
            {
                var architectures = function!["Architectures"]!.ToObject<JArray>();

                var functionObject = function.ToObject<JObject>();

                //var name = functionObject["Name"].ToString();
                var importDll = functionObject["DllImport"].ToString();

                var func = GenerateFunction(functionObject);

                if (architectures.Count > 0)
                {
                    var arcs = architectures.ToList<JToken>().Select(a => GetNativeArch(a.ToString()));
                    outputContent.AppendLine($"#if {string.Join(" || ", arcs)}");
                }

                AddTabs(indentLevel + 1, ref outputContent);
                if (importDll.Equals("XAudio2_8"))
                    importDll = "XAudio2";

                string importDllName = importDll;
                if (!importDll.Equals("D3DCOMPILER_47.dll"))
                    importDllName = importDll.Replace(".dll", ".lib").Replace(".cpl", ".lib");

                outputContent.AppendLine($"[Import(\"{importDllName}\"), CLink, CallingConvention(.Stdcall)]");
                AddTabs(indentLevel + 1, ref outputContent);
                outputContent.AppendLine($"public static extern {func.ReturnType.TypeName} {func.Name}({func.GetParamsString()});");

                var unicodeAlias = unicodeAliasNames.FirstOrDefault(ua => ua.Equals(func.Name.TrimEnd('A')));
                if (unicodeAlias != null)
                {
                    AddTabs(indentLevel + 1, ref outputContent);
                    outputContent.AppendLine($"public static {func.ReturnType.TypeName} {unicodeAlias}({func.GetParamsString()}) => {func.Name}({func.GetParamsNames()});");
                }

                outputContent.AppendLine();


                if (architectures!.Count > 0)
                {
                    outputContent.AppendLine("#endif");
                }
            }
        }

        private static void _ProcessConstants(JArray constants, ref StringBuilder outputContent, int indentLevel)
        {
            foreach (var constant in constants)
            {
                var constantObject = constant.ToObject<JObject>();

                var name = constantObject!["Name"]!.ToString();
                var typeName = constantObject!["Type"]!["Name"]!.ToString();
                var typeKind = constantObject!["Type"]!["Kind"]!.ToString();
                var typeApi = constantObject!["Type"]?["Api"]?.ToString();
                var value = constantObject!["Value"]!.ToString();
                var valueType = constantObject!["ValueType"]!.ToString();

                if (name == "U8_LEAD4_T1_BITS")
                {
                    int x = 1;
                }

                if (valueType == "PropertyKey")
                {
                    var valueObject = constantObject!["Value"]!.ToObject<JObject>();

                    AddTabs(indentLevel + 1, ref outputContent);

                    string guid = String.Empty;
                    string pid = String.Empty;

                    foreach (var v in valueObject)
                    {
                        if (v.Key == "Fmtid")
                        {
                            guid = v.Value.ToString();
                        }
                        if (v.Key == "Pid")
                        {
                            pid = v.Value.ToString();
                        }
                    }

                    outputContent.AppendLine($"public const {GetType(typeName)} {name} = .({FormatGuid(guid)}, {pid});");
                }
                else if (typeKind == "Native" || typeName == "HRESULT")
                {
                    AddTabs(indentLevel + 1, ref outputContent);
                    outputContent.AppendLine($"public const {GetType(typeName)} {name} = {GetValue(typeName, value)};");
                }
                else if (typeKind == "ApiRef")
                {
                    //try
                    //{
                    //    var valueObject = constantObject!["Value"]!.ToObject<JObject>();

                    //    AddTabs(indentLevel + 1, ref outputContent);

                    //    outputContent.Append($"public static {GetType(typeName)} {name} = .(){{");
                    //    outputContent.AppendLine();
                    //    foreach (var fieldValue in valueObject!)
                    //    {
                    //        AddTabs(indentLevel + 2, ref outputContent);
                    //        outputContent.AppendLine($"{fieldValue.Key} = {fieldValue!.Value!.ToString()},");
                    //    }
                    //    AddTabs(indentLevel + 1, ref outputContent);
                    //    outputContent.AppendLine("};");
                    //}
                    //catch (Exception)
                    //{
                    AddTabs(indentLevel + 1, ref outputContent);
                    if (name == "INVALID_SOCKET" && typeName == "SOCKET")
                        outputContent.Append($"public const {GetType(typeName)} {name} = {typeName}.MaxValue;");
                    else
                        outputContent.Append($"public const {GetType(typeName)} {name} = {GetValue(typeName, value)};");
                    //}
                    outputContent.AppendLine($"");
                }
                else
                {
                    throw new Exception($"Type Kind '{typeKind}' not handled.");
                }
            }
        }

        class TypesBuilder
        {
            public StringBuilder NativeTypedefs = new StringBuilder();
            public StringBuilder Enums = new StringBuilder();
            public StringBuilder StructsOrUnions = new StringBuilder();
            public StringBuilder Com = new StringBuilder();
            public StringBuilder FunctionPointers = new StringBuilder();
            public StringBuilder ComClassIDs = new StringBuilder();
        }

        private static void _ProcessTypes(JArray types, JObject parentType, TypesBuilder builder, int indentLevel, string api, ref HashSet<string> referencedApis)
        {
            foreach (var type in types!)
            {
                var typeKind = type!["Kind"]!.ToString();

                StringBuilder current = null;
                StringBuilder tempBuilder = new StringBuilder();
                if (typeKind == "NativeTypedef")
                {
                    __ProcessNativeTypedef(type.ToObject<JObject>(), parentType, ref tempBuilder, indentLevel);
                    current = builder.NativeTypedefs;
                }
                else if (typeKind == "Enum")
                {
                    __ProcessEnum(type.ToObject<JObject>(), parentType, ref tempBuilder, indentLevel);
                    current = builder.Enums;
                }
                else if (typeKind == "Struct")
                {
                    var structObject = type.ToObject<JObject>();
                    __ProcessStructOrUnion(structObject, parentType, ref tempBuilder, indentLevel, api, out bool isAnonymousStruct, ref referencedApis);
                    current = builder.StructsOrUnions;
                }
                else if (typeKind == "Union")
                {
                    var unionObject = type.ToObject<JObject>();
                    __ProcessStructOrUnion(unionObject, parentType, ref tempBuilder, indentLevel, api, out bool isAnonymousUnion, ref referencedApis);
                    current = builder.StructsOrUnions;
                }
                else if (typeKind == "Com")
                {
                    var comObject = type.ToObject<JObject>();
                    __ProcessCOMType(comObject, parentType, ref tempBuilder, indentLevel, api);
                    current = builder.Com;
                }
                else if (typeKind == "FunctionPointer")
                {
                    var functionPtr = type.ToObject<JObject>();
                    __ProcessFunctionPtr(functionPtr, parentType, ref tempBuilder, indentLevel);
                    current = builder.FunctionPointers;
                }
                else if (typeKind == "ComClassID")
                {
                    var comClassId = type.ToObject<JObject>();
                    __ProcessCOMClassId(comClassId, parentType, ref tempBuilder, indentLevel);
                    current = builder.ComClassIDs;
                }
                else
                {
                    throw new Exception($"Type Kind '{typeKind}' not handled yet.");
                }

                var architectures = type!["Architectures"]!.ToObject<JArray>();

                if (architectures!.Count > 0)
                {
                    var arcs = architectures.ToList<JToken>().Select(a => GetNativeArch(a.ToString()));
                    current.AppendLine($"#if {string.Join(" || ", arcs)}");
                }
                current.Append(tempBuilder);
                if (architectures!.Count > 0)
                {
                    current.AppendLine("#endif");
                }
                current.AppendLine();
            }
        }

        private static void __ProcessNativeTypedef(JObject typedefObject, JObject parentStructType, ref StringBuilder outputContent, int indentLevel)
        {
            var name = typedefObject["Name"]!.ToString();
            var defKind = typedefObject["Def"]!["Kind"]!.ToString();

            if (defKind == "Native")
            {
                var defName = typedefObject["Def"]!["Name"]!.ToString();
                var nativeType = GetNativeType(defName) ?? throw new Exception($"Native type '{defName}' not mapped yet.");

                outputContent.AppendLine($"typealias {name} = {nativeType};");

                if (!NativeTypedefs.ContainsKey(name))
                    NativeTypedefs.Add(name, nativeType);
            }
            else if (defKind == "PointerTo")
            {
                var childKind = typedefObject["Def"]!["Child"]!["Kind"]!.ToString();
                var childName = typedefObject["Def"]!["Child"]!["Name"]!.ToString();

                var nativeType = GetNativeType(childName) ?? throw new Exception($"Native type '{childName}' not mapped yet.");

                outputContent.AppendLine($"typealias {name} = {nativeType}*;");

                if (!NativeTypedefs.ContainsKey(name))
                    NativeTypedefs.Add(name, $"{nativeType}*");
            }
            else
            {
                throw new Exception($"NativeTypedef '{name}:{defKind}' not handled.");
            }
        }

        private static void __ProcessEnum(JObject enumObject, JObject parentStructType, ref StringBuilder outputContent, int indentLevel)
        {
            var name = enumObject["Name"]!.ToString();
            var baseType = enumObject["IntegerBase"]!.ToString();
            var values = enumObject["Values"];


            outputContent.AppendLine($"");
            AddTabs(indentLevel, ref outputContent);
            outputContent.AppendLine($"[AllowDuplicates]");
            AddTabs(indentLevel, ref outputContent);

            if (string.IsNullOrEmpty(baseType))
            {
                outputContent.AppendLine($"public enum {name}");
            }
            else
            {
                outputContent.AppendLine($"public enum {name} : {GetNativeType(baseType) ?? throw new Exception($"Cannot get Native Type for '{baseType}'.")}");
            }
            AddTabs(indentLevel, ref outputContent);
            outputContent.AppendLine("{");

            foreach (var value in values!)
            {
                var valueObject = value.ToObject<JObject>();

                var valueName = valueObject!["Name"]!.ToString();
                var valueValue = valueObject!["Value"]!.ToString();

                AddTabs(indentLevel + 1, ref outputContent);
                if (valueValue != null)
                    outputContent.AppendLine($"{valueName} = {valueValue},");
                else
                    outputContent.AppendLine($"{valueName},");
            }
            AddTabs(indentLevel, ref outputContent);
            outputContent.AppendLine("}");
        }

        private static List<string> __ProcessStructOrUnion(JObject structOrInion, JObject parentStructType, ref StringBuilder outputContent, int indentLevel, string api, out bool isAnonymous, ref HashSet<string> referencedApis)
        {
            var kind = structOrInion["Kind"]!.ToString();
            var name = structOrInion["Name"]!.ToString();
            var packingSize = structOrInion["PackingSize"]!.ToString();
            var fields = structOrInion["Fields"]!.ToObject<JArray>();
            var nestedTypes = structOrInion!["NestedTypes"]!.ToObject<JArray>();

            List<string> returnFamUnionMembers = new List<string>();

            isAnonymous = name.Contains("_Anonymous") && (name.Contains("__Struct") || name.Contains("__Union"));
            bool isStruct = kind == "Struct";

            StringBuilder membersWriter = new StringBuilder();
            List<string> attributes = new List<string>();

            // process members first so we can determine attributes
            {
                var processedNestedTypes = new HashSet<string>();
                if (nestedTypes != null)
                {
                    foreach (var nestedType in nestedTypes)
                    {
                        var famUnionMembers = __ProcessStructOrUnion(nestedType.ToObject<JObject>(), structOrInion, ref membersWriter, indentLevel + 1, api, out bool _, ref referencedApis);

                        //if (famUnionMembers.Count > 0)
                        //{
                        //    attributes.Add($"FlexibleArray({string.Join(", ", famUnionMembers.Select(f => $"\"{f}\"").ToList())})");
                        //}

                        processedNestedTypes.Add(nestedType["Name"].ToString());
                    }
                }

                //bool allFieldsAreFams = true;
                //foreach (var field in fields)
                //{
                //    var fieldType = field["Type"]!.ToObject<JObject>();
                //    var fieldTypeInfo = GetTypeInfo(fieldType);
                //    if (!fieldTypeInfo.type.EndsWith("[ANYSIZE_ARRAY]"))
                //    {
                //        allFieldsAreFams = false;
                //    }
                //}

                foreach (var field in fields)
                {
                    var fieldName = field["Name"].ToString();
                    var fieldType = field["Type"].ToObject<JObject>();
                    var fieldTypeKind = fieldType["Kind"]!.ToString();

                    var fieldTypeInfo = GetTypeInfo(fieldType);

                    if (string.IsNullOrEmpty(fieldTypeInfo.type))
                    {
                        throw new Exception();
                    }

                    var finalFieldName = ReplaceNameIfReservedWord(fieldName);
                    string fieldVisibility = "public";

                    if (fieldTypeInfo.kind == "Array" && fieldTypeInfo.type.EndsWith("[ANYSIZE_ARRAY]"))
                    {
                        //AddTabs(indentLevel + 1, ref membersWriter);
                        //membersWriter.AppendLine("[Warn(\"Consider accessing this structure with System.Interop.FlexibleArray<>\")]");

                        var propertyFieldName = finalFieldName;
                        fieldVisibility = "private";
                        finalFieldName += "_impl";

                        AddTabs(indentLevel + 1, ref membersWriter);
                        membersWriter.AppendLine($"public {fieldTypeInfo.type.Replace("[ANYSIZE_ARRAY]", "*")} {propertyFieldName} mut => &{finalFieldName};");
                        //referencedApis.Add("System.Interop");
                        if (isStruct)
                        {
                            //attributes.Add($"FlexibleArray(\"{finalFieldName}\")");
                            //fieldVisibility = "private";
                            //finalFieldName += "_impl";
                        }
                        else
                        {
                            //if (allFieldsAreFams)
                            //{
                            returnFamUnionMembers.Add(finalFieldName);
                            //finalFieldName += "_impl";
                            //}
                            //else
                            //{
                            //    fieldTypeInfo.type = fieldTypeInfo.type.Replace("[ANYSIZE_ARRAY]", "[ANYSIZE_ARRAY]");
                            //}
                        }
                    }
                    AddTabs(indentLevel + 1, ref membersWriter);
                    if (fieldTypeInfo.type.Contains("_Anonymous") && fieldName.Contains("Anonymous"))
                        membersWriter.AppendLine($"{fieldVisibility} using {fieldTypeInfo.type} {finalFieldName};");
                    else
                        membersWriter.AppendLine($"{fieldVisibility} {fieldTypeInfo.type} {finalFieldName};");
                }

                if (nestedTypes != null)
                {
                    foreach (var nestedType in nestedTypes)
                    {
                        var nestedTypeName = nestedType["Name"]!.ToString();
                        if (!processedNestedTypes.Contains(nestedTypeName))
                        {
                            //throw new Exception($"Nested Type '{nestedTypeName}' was not processed.");
                            Console.WriteLine($"Nested Type '{nestedTypeName}' was not processed.");
                        }
                    }
                    //_ProcessTypes(nestedTypes, parentStructType, ref outputContent, indentLevel + 1, ref usings);
                }
            }


            AddTabs(indentLevel, ref outputContent);
            outputContent.Append(isStruct ? "[CRepr" : "[CRepr, Union");
            if (packingSize != "0")
            {
                outputContent.Append($", Packed({packingSize})");
            }
            foreach (var attribute in attributes)
            {
                outputContent.Append($", {attribute}");
            }
            outputContent.Append("]");
            outputContent.AppendLine();
            AddTabs(indentLevel, ref outputContent);
            outputContent.AppendLine($"{(isAnonymous ? /*"private"*/"public" : "public")} {(isStruct ? "struct" : "struct" /*union*/)} {name}");
            AddTabs(indentLevel, ref outputContent);
            outputContent.AppendLine("{");

            outputContent.Append(membersWriter);

            AddTabs(indentLevel, ref outputContent);
            outputContent.AppendLine("}");

            if (!ProcessedStructsOrUnions.ContainsKey(api))
            {
                ProcessedStructsOrUnions.Add(api, new List<JObject>());
            }
            ProcessedStructsOrUnions[api].Add(structOrInion);

            return returnFamUnionMembers;
        }

        private static void __ProcessCOMType(JObject comObject, JObject parentType, ref StringBuilder outputContent, int indentLevel, string api)
        {
            var name = comObject["Name"].ToString();
            var guid = comObject["Guid"].ToString();
            var @interface = comObject["Interface"].ToObject<JObject>();
            var methods = comObject["Methods"].ToObject<JArray>();

            string interfaceName = null;
            if (@interface != null)
            {
                interfaceName = @interface["Name"].ToString();
            }

            outputContent.Append($"[CRepr]struct {name}");
            if (!String.IsNullOrEmpty(interfaceName))
            {
                outputContent.Append($" : {interfaceName}");
            }
            outputContent.AppendLine();
            outputContent.AppendLine("{");

            if (!string.IsNullOrEmpty(guid))
            {
                AddTabs(indentLevel + 1, ref outputContent);
                outputContent.AppendLine($"public new const Guid IID = {FormatGuid(guid)};");

                outputContent.AppendLine();

            }

            AddTabs(indentLevel + 1, ref outputContent);
            if (!string.IsNullOrEmpty(interfaceName))
            {

                outputContent.AppendLine($"public new VTable* VT {{ get => (.)mVT; }}");
                outputContent.AppendLine();

                AddTabs(indentLevel + 1, ref outputContent);
                outputContent.Append($"[CRepr]public struct VTable : {interfaceName}.VTable");
            }
            else
            {
                outputContent.AppendLine($"public VTable* VT {{ get => (.)mVT; }}");
                outputContent.AppendLine();
                AddTabs(indentLevel + 1, ref outputContent);
                outputContent.AppendLine($"protected VTable* mVT;");
                outputContent.AppendLine();

                AddTabs(indentLevel + 1, ref outputContent);
                outputContent.Append($"[CRepr]public struct VTable");
            }
            outputContent.AppendLine();
            AddTabs(indentLevel + 1, ref outputContent);
            outputContent.AppendLine("{");

            List<String> usedNames = new List<String>();
            List<String> prettyMethods = new List<string>();

            var getNextUnusedName = (List<String> un, string n) =>
            {
                string name = n;
                int nextPostfix = 0;
                while (usedNames.Contains(name))
                {
                    name = n + nextPostfix;
                    nextPostfix++;
                }
                return name;
            };

            foreach (var method in methods)
            {
                var methodObject = method.ToObject<JObject>();

                if (name == "ID3D11DeviceContext1" && methodObject["Name"].ToString() == "ClearView")
                {
                    int g = 1;
                }

                var func = GenerateFunction(methodObject);

                //var methodName = methodObject["Name"].ToString();

                //var returnType = GetTypeFromJObject(methodObject["ReturnType"].ToObject<JObject>());

                //var @params = methodObject["Params"].ToObject<JArray>();

                var finalMethodName = getNextUnusedName(usedNames, func.Name);

                if (replaceCOMMethods.Contains(finalMethodName))
                {
                    finalMethodName = $"COM_{finalMethodName}";
                }

                bool returnIsOutputParam = false;
                if (func.ReturnType.Kind == "ApiRef" && !NativeTypeDefsAndEnums.Contains(func.ReturnType.TypeName))
                {
                    if (func.ReturnType.TargetKind != "Com")
                    {
                        returnIsOutputParam = true;
                    }
                    Console.WriteLine("{0} - {1}::{2} - {3}", name, func.Name, func.ReturnType.TypeName, func.ReturnType.TargetKind);
                }

                AddTabs(indentLevel + 2, ref outputContent);
                string fullParamsString = string.Join(", ", func.GetParamsString());

                if (returnIsOutputParam)
                {
                    outputContent.AppendLine($"protected new function [CallingConvention(.Stdcall)] void(SelfOuter* self, out {func.ReturnType.TypeName} @return{(func.HasParams ? ", " : "")}{fullParamsString}) {finalMethodName};");

                    var prettyMethod = $"public {func.ReturnType.TypeName} {func.Name}({fullParamsString}) mut => VT.[Friend]{finalMethodName}(&this, ..?{(func.HasParams ? ", " : "")}{func.GetParamsNames()});";

                    prettyMethods.Add(prettyMethod);
                }
                else
                {
                    outputContent.AppendLine($"protected new function [CallingConvention(.Stdcall)] {func.ReturnType.TypeName}(SelfOuter* self{(func.HasParams ? ", " : "")}{fullParamsString}) {finalMethodName};");

                    var prettyMethod = $"public {func.ReturnType.TypeName} {func.Name}({fullParamsString}) mut => VT.[Friend]{finalMethodName}(&this{(func.HasParams ? ", " : "")}{func.GetParamsNames()});";

                    prettyMethods.Add(prettyMethod);
                }

                usedNames.Add(finalMethodName);
            }

            AddTabs(indentLevel + 1, ref outputContent);
            outputContent.AppendLine("}");

            outputContent.AppendLine();

            foreach (var prettyMethod in prettyMethods)
            {
                outputContent.AppendLine();
                AddTabs(indentLevel + 1, ref outputContent);
                outputContent.AppendLine(prettyMethod);
            }

            outputContent.AppendLine("}");

            //outputContent.AppendLine("}");

        }

        private static void __ProcessCOMClassId(JObject comObject, JObject parentType, ref StringBuilder outputContent, int indentLevel)
        {
            var name = comObject["Name"].ToString();
            var guid = comObject["Guid"].ToString();

            AddTabs(indentLevel + 1, ref outputContent);
            outputContent.AppendLine($"public const Guid CLSID_{name} = {FormatGuid(guid)};");
            outputContent.AppendLine();
        }

        private static void __ProcessFunctionPtr(JObject comObject, JObject parentType, ref StringBuilder outputContent, int indentLevel)
        {
            var name = comObject["Name"].ToString();

            var func = GenerateFunction(comObject);

            outputContent.AppendLine($"public function {func.ReturnType.TypeName} {func.Name}({func.GetParamsString()});");
        }

        private class FunctionParameter
        {
            public String TypeName { get; set; }
            public String Name { get; set; }
            public String CallName { get; set; }
            public List<String> Attributes { get; } = new List<String>();
        }

        private class FunctionReturnType
        {
            public string TypeName { get; set; }
            public string Kind { get; set; }
            public string TargetKind { get; set; }
        }

        private class Function
        {
            public string Name { get; set; }
            public FunctionReturnType ReturnType { get; set; }
            public List<FunctionParameter> Parameters { get; set; } = new();

            private bool baked = false;

            private string bakedParamsString = null;
            private string bakedNamesString = null;

            private void Bake()
            {
                foreach (var parameter in Parameters)
                {
                    parameter.CallName = parameter.Name;

                    // if (parameter.Attributes.Contains("Const") && parameter.Attributes.Contains("In"))
                    {
                        //parameter.TypeName = $"ref {parameter.TypeName}";
                        //parameter.TypeName.TrimEnd('*');
                        //parameter.CallName = $"ref {parameter.Name}";
                        if (parameter.TypeName.StartsWith("ref "))
                        {
                            parameter.CallName = $"ref {parameter.CallName}";
                        }
                    }
                }

                bakedParamsString = string.Join(", ", Parameters.Select(p => $"{p.TypeName} {p.Name}").ToList());
                bakedNamesString = string.Join(", ", Parameters.Select(p => p.CallName).ToList()); ;

                baked = true;
            }

            public string GetParamsString()
            {
                if (!baked)
                {
                    Bake();
                }

                return bakedParamsString;
            }

            public string GetParamsNames()
            {
                if (!baked)
                {
                    Bake();
                }

                return bakedNamesString;
            }

            public bool HasParams => Parameters.Count > 0;
        }

        private static Function GenerateFunction(JObject functionObject)
        {
            var name = functionObject["Name"].ToString();

            var returnType = GetTypeInfo(functionObject["ReturnType"].ToObject<JObject>());

            var @params = functionObject["Params"].ToObject<JArray>();

            Function function = new Function()
            {
                Name = name,
                ReturnType = new FunctionReturnType
                {
                    TypeName = returnType.type,
                    Kind = returnType.kind,
                    TargetKind = returnType.targetKind
                },
            };

            if (@params != null)
            {
                foreach (var @param in @params)
                {
                    var paramName = @param["Name"].ToString();
                    var paramType = GetParamTypeInfo(@param.ToObject<JObject>());

                    var functionParameter = new FunctionParameter()
                    {
                        Name = ReplaceNameIfReservedWord(paramName),
                        TypeName = paramType.type,
                    };

                    functionParameter.Attributes.AddRange(paramType.attributes);

                    function.Parameters.Add(functionParameter);
                }
            }

            return function;
        }

        private static (string type, List<string> attributes) GetParamTypeInfo(JObject paramObject)
        {
            //var typeKind = typeObject["Kind"].ToString();
            var attrs = new List<String>();

            var typeObject = paramObject["Type"].ToObject<JObject>();

            var paramObjectAttrs = paramObject["Attrs"];
            if (paramObjectAttrs != null)
            {
                attrs = paramObjectAttrs.ToObject<JArray>().Select(a => a.ToString()).ToList();
            }

            var typeInfo = GetTypeInfo(typeObject);

            if (typeInfo.kind == "PointerTo" && typeInfo.childKind == "Native")
            {

                if ((attrs.Count == 2 && attrs.Contains("In") && attrs.Contains("Const")) && typeInfo.type != "void*")
                {
                    if (typeInfo.type == "Guid*")
                    {
                        typeInfo.type = typeInfo.type.TrimEnd('*');
                        typeInfo.type = $"in {typeInfo.type}";
                    }
                }
                //else if (typeInfo.type == "Guid*")
                //{
                //    typeInfo.type = typeInfo.type.TrimEnd('*');
                //    typeInfo.type = $"in {typeInfo.type}";
                //}
            }

            return (typeInfo.type, attrs);
        }

        private static (string type, string kind, string childKind, string targetKind) GetTypeInfo(JObject typeObject)
        {
            var typeKind = typeObject["Kind"].ToString();
            var typeChild = typeObject["Child"];
            string name = string.Empty;
            if (typeObject["Name"] != null)
                name = typeObject["Name"].ToString();

            if (name.Contains("IImageList") || name.Contains("D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE"))
            {
                var paramTypeApi = string.Empty;
                var typeChildObject = typeObject["Child"]?.ToObject<JObject>();
                if (typeChildObject != null)
                {
                    paramTypeApi = typeChildObject["Api"].ToString();
                }
                else
                {
                    paramTypeApi = typeObject["Api"]?.ToString();
                }
                if (!string.IsNullOrEmpty(paramTypeApi))
                {
                    name = $"{RootNamespace}.{paramTypeApi}.{name}";
                }
            }

            var targetKind = string.Empty;
            if (typeObject["TargetKind"] != null)
                targetKind = typeObject["TargetKind"].ToString();

            string childKind = string.Empty;
            if (typeChild != null)
            {
                childKind = typeChild["Kind"].ToString();
            }

            if (typeKind == "Native")
            {
                return (GetNativeType(name), typeKind, childKind, targetKind);
            }
            else if (typeKind == "NativeTypedef")
            {
                return (GetNativeTypedef(name), typeKind, childKind, targetKind);
            }
            else if (typeKind == "ApiRef")
            {
                if (targetKind == "Com")
                    return ($"{name}*", typeKind, childKind, targetKind);
                return (name, typeKind, childKind, targetKind);
            }
            else if (typeKind == "PointerTo")
            {
                var childObject = typeObject["Child"].ToObject<JObject>();
                //var attrArray = typeObject["Attrs"].ToObject<JArray>();
                //List<string> attrs = attrArray.Select(attr => attr.ToString()).ToList(); // todo: use this
                var type = GetTypeInfo(childObject);
                //if (type.type == "Guid" || type.type == "PWSTR")
                //    return (type.type, typeKind);
                return ($"{type.type}*", typeKind, childKind, targetKind);
            }
            else if (typeKind == "Array")
            {
                var childObject = typeObject["Child"].ToObject<JObject>();
                var type = GetTypeInfo(childObject);

                string size = "ANYSIZE_ARRAY";

                var shape = typeObject["Shape"].ToObject<JObject>();
                if (shape != null)
                {
                    size = shape["Size"].ToString();
                }

                return ($"{type.type}[{size}]", typeKind, childKind, targetKind);
            }
            else if (typeKind == "LPArray")
            {
                var childObject = typeObject["Child"].ToObject<JObject>();
                int countConst = typeObject["CountConst"].ToObject<int>();
                int countParamsIndex = typeObject["CountParamIndex"].ToObject<int>();
                var type = GetTypeInfo(childObject);
                return ($"{type.type}*", typeKind, childKind, targetKind);
            }
            else if (typeKind == "MissingClrType")
            {
                return ("void*", typeKind, childKind, targetKind);
            }
            else
            {
                throw new Exception($"Unhandled type kind: {typeKind}");
            }
        }

        private static void AddTabs(int numTabs, ref StringBuilder builder)
        {
            for (int i = 0; i < numTabs; i++)
            {
                builder.Append("\t");
            }
        }

        public static string GetValue(string type, string value)
        {
            if (type == "Guid")
            {
                var guid = Guid.Parse(value).ToString("N");

                var a = "0x" + guid.Substring(0, 8);
                var b = "0x" + guid.Substring(8, 4);
                var c = "0x" + guid.Substring(12, 4);
                var d = "0x" + guid.Substring(16, 2);
                var e = "0x" + guid.Substring(18, 2);
                var f = "0x" + guid.Substring(20, 2);
                var g = "0x" + guid.Substring(22, 2);
                var h = "0x" + guid.Substring(24, 2);
                var i = "0x" + guid.Substring(26, 2);
                var j = "0x" + guid.Substring(28, 2);
                var k = "0x" + guid.Substring(30, 2);

                return $".({a}, {b}, {c}, {d}, {e}, {f}, {g}, {h}, {i}, {j}, {k})";
            }

            if (type == "String")
            {
                //var strObject = $"{{\"value\":\"{value}\"}}";
                //var x = JObject.Parse(strObject);
                //string stringValue = x["value"].ToObject<string>();

                //stringValue = stringValue.Replace("\\", "\\\\");

                //if (value.Contains("\\u"))
                //{
                //    var occurences = new Regex(@"\\u([a-fA-F_0-9..]{4})");

                //    MatchCollection matches = occurences.Matches(value);

                //    foreach (var match in matches)
                //    {
                //        var referencedApi = match.ToString();
                //    }


                //    int p = 1;
                //}

                value = Regex.Replace(value, @"\\u([a-fA-F_0-9..]{4})", "\\u{$1}");
                return $"\"{value}\"";
            }

            if (type == "PWSTR")
            {
                return $"(PWSTR)(void*){value}";
            }

            if (type == "PSTR")
            {
                return $"(PSTR)(void*){value}";
            }

            if (type == "float" || type == "Single")
            {
                return $"{value}f";
            }

            return value;
        }

        public static string FormatGuid(string value)
        {
            var guid = Guid.Parse(value).ToString("N");

            var a = "0x" + guid.Substring(0, 8);
            var b = "0x" + guid.Substring(8, 4);
            var c = "0x" + guid.Substring(12, 4);
            var d = "0x" + guid.Substring(16, 2);
            var e = "0x" + guid.Substring(18, 2);
            var f = "0x" + guid.Substring(20, 2);
            var g = "0x" + guid.Substring(22, 2);
            var h = "0x" + guid.Substring(24, 2);
            var i = "0x" + guid.Substring(26, 2);
            var j = "0x" + guid.Substring(28, 2);
            var k = "0x" + guid.Substring(30, 2);

            return $".({a}, {b}, {c}, {d}, {e}, {f}, {g}, {h}, {i}, {j}, {k})";
        }

        public static string GetNativeType(string type)
        {
            if (NativeTypes.ContainsKey(type))
                return NativeTypes[type];

            return null;
        }
        public static string GetNativeTypedef(string type)
        {
            if (NativeTypedefs.ContainsKey(type))
                return NativeTypedefs[type];

            return null;
        }

        public static string GetFundamentalType(string type)
        {
            if (FundamentalTypes.ContainsKey(type))
                return FundamentalTypes[type];

            return null;
        }

        public static string GetType(string type)
        {
            return GetNativeType(type) ?? GetFundamentalType(type) ?? type;
        }

        private static string GetNativeArch(string inputArch)
        {
            if (inputArch == "X86")
                return "BF_32_BIT";
            if (inputArch == "X64")
                return "BF_64_BIT";
            if (inputArch == "Arm64")
                return "BF_ARM_64";

            throw new Exception($"Input architecture '{inputArch}' not handled.");
        }

        private static string ReplaceNameIfReservedWord(string name)
        {
            if (ReservedWords.Contains(name))
                return $"@{name}";
            return name;
        }
    }
}