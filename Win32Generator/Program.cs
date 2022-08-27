using CommandLine;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
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
            {"Char", "char8" },
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

                var outputPath = Path.Combine(options.OutputDir, dirString);

                var apiFile = new APIFile(filePart, apiString, file, outputPath);

                APIFiles.Add(apiFile);

                if (apiString.Contains("Storage.EnhancedStorage"))
                {
                    int x = 1;
                }


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
            }

            APIFiles.OrderBy(f => f.Dependencies.Count);

            ProcessAPIFiles(APIFiles);
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

            //foreach (var dependency in apiFile.Dependencies)
            //{
            //    var dependencyFile = APIFiles.FirstOrDefault(a => a.Api == dependency);
            //    if (dependencyFile != null)
            //    {
            //        if (ProcessedAPIFiles.Contains(dependencyFile))
            //            continue;
            //        ProcessAPIFile(dependencyFile);
            //    }
            //    else
            //    {
            //        throw new Exception($"Unknown dependency node '{dependency}'.");
            //    }
            //}

            {
                var constants = apiFile.Content.GetValue("Constants")!.ToObject<JArray>();
                var types = apiFile.Content.GetValue("Types")!.ToObject<JArray>();
                var functions = apiFile.Content.GetValue("Functions")!.ToObject<JArray>();
                var unicodeAliases = apiFile.Content.GetValue("UnicodeAliases")!.ToObject<JArray>();

                var typesBuilder = new TypesBuilder();
                var constantsContent = new StringBuilder();
                var functionsContent = new StringBuilder();
                var unicodeAliasesContent = new StringBuilder();


                _ProcessTypes(types, null, typesBuilder, 0, apiFile.Api);
                _ProcessConstants(constants, ref constantsContent, 0);
                _ProcessFunctions(functions, ref functionsContent, 0);
                _ProcessUnicodeAliases(unicodeAliases, ref unicodeAliasesContent, 0);

                var outputContent = new StringBuilder();

                if (apiFile.Dependencies.Count > 0)
                {
                    foreach (var dedepndencyApi in apiFile.Dependencies)
                    {
                        outputContent.AppendLine($"using {RootNamespace}.{dedepndencyApi};");
                    }
                }

                outputContent.AppendLine($"using System;");
                outputContent.AppendLine($"");

                var fileName = Path.GetFileName(apiFile.InputPath);
                var @namespace = $"{RootNamespace}.{apiFile.Api}";

                outputContent.AppendLine($"namespace {@namespace};");

                // Write out constants
                if (constantsContent.Length > 0)
                {
                    outputContent.AppendLine("#region Constants");
                    outputContent.AppendLine("public static");
                    outputContent.AppendLine("{");

                    outputContent.Append(constantsContent.ToString());

                    outputContent.AppendLine("}");
                    outputContent.AppendLine("#endregion");
                }
                outputContent.AppendLine();

                // native typedefs

                outputContent.AppendLine("#region TypeDefs");
                outputContent.Append(typesBuilder.NativeTypedefs);
                outputContent.AppendLine("#endregion");
                outputContent.AppendLine();

                // enums
                outputContent.AppendLine("#region Enums");
                outputContent.Append(typesBuilder.Enums);
                outputContent.AppendLine("#endregion");
                outputContent.AppendLine();

                // function pointers
                outputContent.AppendLine("#region Function Pointers");
                //outputContent.AppendLine("public static");
                //outputContent.AppendLine("{");
                outputContent.Append(typesBuilder.FunctionPointers);
                //outputContent.AppendLine("}");
                outputContent.AppendLine("#endregion");
                outputContent.AppendLine();

                // structs and unions
                outputContent.AppendLine("#region Structs");
                outputContent.Append(typesBuilder.StructsOrUnions);
                outputContent.AppendLine("#endregion");
                outputContent.AppendLine();

                // com class ids
                outputContent.AppendLine("#region COM Class IDs");
                outputContent.AppendLine("public static");
                outputContent.AppendLine("{");
                outputContent.Append(typesBuilder.ComClassIDs);
                outputContent.AppendLine("}");
                outputContent.AppendLine("#endregion");
                outputContent.AppendLine();

                // com
                outputContent.AppendLine("#region COM Types");
                outputContent.Append(typesBuilder.Com);
                outputContent.AppendLine("#endregion");
                outputContent.AppendLine();


                // Frite out functions
                if (functionsContent.Length > 0)
                {
                    outputContent.AppendLine("#region Functions");

                    outputContent.AppendLine("public static");
                    outputContent.AppendLine("{");
                    outputContent.Append(functionsContent.ToString());
                    outputContent.AppendLine("}");
                    outputContent.AppendLine("#endregion");
                    outputContent.AppendLine();
                }

                // Write out unicode aliases
                if (unicodeAliasesContent.Length > 0)
                {
                    outputContent.AppendLine("#region Aliases");
                    outputContent.Append(unicodeAliasesContent.ToString());
                    outputContent.AppendLine("#endregion");
                    outputContent.AppendLine();
                }

                if (!Directory.Exists(apiFile.OutputPath))
                    Directory.CreateDirectory(apiFile.OutputPath);

                var outputFilePath = Path.Join(apiFile.OutputPath, apiFile.Name + ".bf");

                File.WriteAllText(outputFilePath, outputContent.ToString());

                //Console.WriteLine($"Generated '{outputFilePath}' from '{apiFile.InputPath}'.");
            }


            ProcessedAPIFiles.Add(apiFile);
            apiFile.Content = null;
        }


        private static void _ProcessUnicodeAliases(JArray unicodeAliases, ref StringBuilder unicodeAliasesContent, int indentLevel)
        {
        }

        private static void _ProcessFunctions(JArray functions, ref StringBuilder outputContent, int indentLevel)
        {
            foreach (var function in functions)
            {

                var architectures = function!["Architectures"]!.ToObject<JArray>();

                var functionObject = function.ToObject<JObject>();

                var name = functionObject["Name"].ToString();
                var importDll = functionObject["DllImport"].ToString();

                var returnType = GetTypeFromJObject(functionObject["ReturnType"].ToObject<JObject>());

                var @params = functionObject["Params"].ToObject<JArray>();

                List<string> paramStrings = new List<string>();

                if (@params != null)
                {
                    foreach (var @param in @params)
                    {
                        var paramName = @param["Name"].ToString();
                        var paramType = GetTypeFromJObject(@param["Type"].ToObject<JObject>());
                        var attrs = @param["Attrs"].ToObject<JArray>();
                        string paramString = string.Empty;
                        List<String> attrStrings = new List<String>();
                        foreach (var attr in attrs)
                        {
                            var attrString = attr.ToString();
                            attrStrings.Add(attrString);
                        }

                       //if (attrStrings.Count == 1 && attrStrings.Contains("Out"))
                       // {
                       //     paramString += $"out ";
                       // }
                        paramString += $"{paramType} {ReplaceNameIfReservedWord(paramName)}";

                        paramStrings.Add(paramString);
                    }
                }

                if (architectures!.Count > 0)
                {
                    var arcs = architectures.ToList<JToken>().Select(a => GetNativeArch(a.ToString()));
                    outputContent.AppendLine($"#if {string.Join(" || ", arcs)}");
                }

                AddTabs(indentLevel + 1, ref outputContent);
                outputContent.AppendLine($"[Import(\"{importDll}.lib\"), CLink, CallingConvention(.Stdcall)]");
                AddTabs(indentLevel + 1, ref outputContent);
                outputContent.AppendLine($"public static extern {returnType} {name}({string.Join(", ", paramStrings)});");
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

                if (typeName == "float" || typeName == "Single")
                {
                    int x = 1;
                }

                if (valueType == "PropertyKey")
                {
                    var valueObject = constantObject!["Value"]!.ToObject<JObject>();

                    AddTabs(indentLevel + 1, ref outputContent);
                    outputContent.Append($"");

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
                    outputContent.AppendLine();

                    //outputContent.Append($"public static {GetType(typeName)} {name} = .(){{");
                    //outputContent.AppendLine();
                    //foreach (var fieldValue in valueObject!)
                    //{
                    //    AddTabs(indentLevel + 2, ref outputContent);
                    //    var fvk = fieldValue.Key;
                    //    if (fieldValue.Key == "Fmtid")
                    //    {
                    //        fvk = "fmtid"; // for some reason, json file has lower case
                    //        outputContent.AppendLine($"{fvk} = {FormatGuid(fieldValue!.Value!.ToString())},");
                    //    }
                    //    else
                    //    {
                    //        fvk = "pid";// for some reason, json file has lower case
                    //        outputContent.AppendLine($"{fvk} = {fieldValue!.Value!.ToString()},");
                    //    }
                    //}
                    //AddTabs(indentLevel + 1, ref outputContent);
                    //outputContent.AppendLine("};");
                }
                else if (typeKind == "Native" || typeName == "HRESULT")
                {
                    AddTabs(indentLevel + 1, ref outputContent);
                    outputContent.AppendLine($"public const {GetType(typeName)} {name} = {GetValue(typeName, value)};");
                    outputContent.AppendLine($"");
                }
                else if (typeKind == "ApiRef")
                {
                    if (typeApi != null)
                    {
                        var parts = typeApi.Split('.');
                        parts = parts.Take(parts.Length - 1).ToArray();
                    }

                    try
                    {
                        var valueObject = constantObject!["Value"]!.ToObject<JObject>();

                        AddTabs(indentLevel + 1, ref outputContent);
                        outputContent.Append($"");

                        outputContent.Append($"public static {GetType(typeName)} {name} = .(){{");
                        outputContent.AppendLine();
                        foreach (var fieldValue in valueObject!)
                        {
                            AddTabs(indentLevel + 2, ref outputContent);
                            outputContent.AppendLine($"{fieldValue.Key} = {fieldValue!.Value!.ToString()},");
                        }
                        AddTabs(indentLevel + 1, ref outputContent);
                        outputContent.AppendLine("};");
                    }
                    catch (Exception)
                    {
                        AddTabs(indentLevel + 1, ref outputContent);
                        outputContent.Append($"public const {GetType(typeName)} {name} = {GetValue(typeName, value)};");
                    }
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

        private static void _ProcessTypes(JArray types, JObject parentType, TypesBuilder builder, int indentLevel, string api)
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
                    __ProcessStructOrUnion(structObject, parentType, ref tempBuilder, indentLevel, api, out bool isAnonymousStruct);
                    current = builder.StructsOrUnions;
                }
                else if (typeKind == "Union")
                {
                    var unionObject = type.ToObject<JObject>();
                    __ProcessStructOrUnion(unionObject, parentType, ref tempBuilder, indentLevel, api, out bool isAnonymousUnion);
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

        private static void __ProcessStructOrUnion(JObject structOrInion, JObject parentStructType, ref StringBuilder outputContent, int indentLevel, string api, out bool isAnonymous)
        {
            var kind = structOrInion["Kind"]!.ToString();
            var name = structOrInion["Name"]!.ToString();
            var packingSize = structOrInion["PackingSize"]!.ToString();
            var fields = structOrInion["Fields"]!.ToObject<JArray>();
            var nestedTypes = structOrInion!["NestedTypes"]!.ToObject<JArray>();

            isAnonymous = name.Contains("_Anonymous") && (name.Contains("__Struct") || name.Contains("__Union"));
            bool isStruct = kind == "Struct";

            AddTabs(indentLevel, ref outputContent);
            outputContent.Append(isStruct ? "[CRepr" : "[CRepr, Union");
            if (packingSize != "0")
            {
                outputContent.Append($", Packed({packingSize})");
            }
            outputContent.Append("]");
            AddTabs(indentLevel, ref outputContent);
            outputContent.AppendLine($"{(isAnonymous ? /*"private"*/"public" : "public")} {(isStruct ? "struct" : "struct" /*union*/)} {name}");
            AddTabs(indentLevel, ref outputContent);
            outputContent.AppendLine("{");

            int bitfieldCount = 0;
            var processedNestedTypes = new HashSet<string>();
            if (nestedTypes != null)
            {
                foreach (var nestedType in nestedTypes)
                {
                    __ProcessStructOrUnion(nestedType.ToObject<JObject>(), structOrInion, ref outputContent, indentLevel + 1, api, out bool _);
                    outputContent.AppendLine();
                    processedNestedTypes.Add(nestedType["Name"].ToString());
                }
            }
            foreach (var field in fields!)
            {
                var fieldName = field["Name"]!.ToString();
                var fieldType = field["Type"]!.ToObject<JObject>();
                var fieldTypeKind = fieldType!["Kind"]!.ToString();

                if (name.Contains("D3D12_TEXTURE_COPY_LOCATION"))
                {
                    int x = 8;
                }

                var ft = GetTypeFromJObject(fieldType);

                if (string.IsNullOrEmpty(ft))
                {
                    throw new Exception();
                }

                AddTabs(indentLevel + 1, ref outputContent);
                if (ft.Contains("_Anonymous_") && fieldName.Contains("Anonymous"))
                    outputContent.AppendLine($"public using {ft} {ReplaceNameIfReservedWord(fieldName)};");
                else
                    outputContent.AppendLine($"public {ft} {ReplaceNameIfReservedWord(fieldName)};");

                //
                //if (fieldTypeKind == "Native")
                //{
                //    var fieldTypeName = fieldType!["Name"]!.ToString();
                //    AddTabs(indentLevel + 1, ref outputContent);
                //    outputContent.AppendLine($"public {GetNativeType(fieldTypeName) ?? throw new Exception($"Field Type '{fieldTypeName}' not mapped to a native type yet.")} {ReplaceNameIfReservedWord(fieldName)};");
                //}
                //else if (fieldTypeKind == "NativeTypedef")
                //{
                //    var fieldTypeName = fieldType!["Name"]!.ToString();
                //    AddTabs(indentLevel + 1, ref outputContent);
                //    outputContent.AppendLine($"public {GetNativeTypedef(fieldTypeName) ?? throw new Exception($"Field Type '{fieldTypeName}' not mapped to a native type yet.")} {ReplaceNameIfReservedWord(fieldName)};");
                //}
                //else if (fieldTypeKind == "ApiRef")
                //{
                //    var fieldTypeName = fieldType!["Name"]!.ToString();


                //    AddTabs(indentLevel + 1, ref outputContent);

                //    if (fieldTypeName.Contains("_Anonymous_"))
                //        outputContent.AppendLine($"public using {fieldTypeName} {ReplaceNameIfReservedWord(fieldName)};");
                //    else
                //        outputContent.AppendLine($"public {fieldTypeName} {ReplaceNameIfReservedWord(fieldName)};");
                //}
                //else if (fieldTypeKind == "Array")
                //{
                //    var childKind = fieldType["Child"]["Kind"]!.ToString();

                //    string fieldTypeName = null;
                //    string childFieldType = null;
                //    int arraySize = 1;

                //    if (childKind == "ApiRef")
                //    {
                //        fieldTypeName = fieldType["Child"]["Name"].ToString();
                //        childFieldType = fieldTypeName;
                //    }
                //    else if (childKind == "NativeTypedef")
                //    {
                //        fieldTypeName = fieldType["Child"]["Name"]!.ToString();
                //        childFieldType = GetNativeTypedef(fieldTypeName);
                //    }
                //    else if (childKind == "Native")
                //    {
                //        fieldTypeName = fieldType["Child"]["Name"]!.ToString();
                //        childFieldType = GetNativeType(fieldTypeName);
                //    }
                //    else if (childKind == "PointerTo")
                //    {
                //        fieldTypeName = fieldType["Child"]["Child"]["Name"]!.ToString();
                //        var innerKind = fieldType["Child"]["Child"]["Kind"]!.ToString();

                //        if (innerKind == "Native")
                //        {
                //            childFieldType = GetNativeType(fieldTypeName);
                //        }
                //        else if (innerKind == "ApiRef")
                //        {
                //            childFieldType = fieldTypeName;
                //        }
                //        else
                //        {
                //            throw new Exception();
                //        }

                //    }
                //    else
                //    {
                //        Console.WriteLine(childKind);
                //        throw new Exception();
                //    }

                //    var shape = fieldType["Shape"].ToObject<JObject>();
                //    if (shape != null)
                //        arraySize = int.Parse(fieldType["Shape"]["Size"].ToString());

                //    if (String.IsNullOrEmpty(childFieldType))
                //    {
                //        throw new Exception();
                //    }

                //    AddTabs(indentLevel + 1, ref outputContent);
                //    outputContent.AppendLine($"public {childFieldType}[{arraySize}] {ReplaceNameIfReservedWord(fieldName)};");
                //}
                //else
                //{
                //    Console.WriteLine($"Struct Field Kind '{fieldTypeKind} - {t}' not handled yet -.");
                //}
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
            AddTabs(indentLevel, ref outputContent);
            outputContent.AppendLine("}");

            if (!ProcessedStructsOrUnions.ContainsKey(api))
            {
                ProcessedStructsOrUnions.Add(api, new List<JObject>());
            }
            ProcessedStructsOrUnions[api].Add(structOrInion);
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

                var methodName = methodObject["Name"].ToString();

                var returnType = GetTypeFromJObject(methodObject["ReturnType"].ToObject<JObject>());

                var @params = methodObject["Params"].ToObject<JArray>();

                var finalMethodName = getNextUnusedName(usedNames, methodName);

                if (replaceCOMMethods.Contains(finalMethodName))
                {
                    finalMethodName = $"COM_{finalMethodName}";
                }

                List<string> paramStrings = new List<string>();
                List<string> paramNames = new List<string>();

                if (@params != null)
                {
                    foreach (var @param in @params)
                    {
                        var paramName = @param["Name"].ToString();

                        var paramType = GetTypeFromJObject(@param["Type"].ToObject<JObject>());

                        if (paramType.Contains("IImageList"))
                        {
                            var paramTypeApi = string.Empty;
                            var typeChild = param["Type"]["Child"]?.ToObject<JObject>();
                            if (typeChild != null)
                            {
                                paramTypeApi = typeChild["Api"].ToString();
                            }
                            else
                            {
                                paramTypeApi = param["Type"]?["Api"]?.ToString();
                            }
                            if (!string.IsNullOrEmpty(paramTypeApi))
                            {
                                paramType = $"{paramTypeApi}.{paramType}";
                            }
                        }
                        var attrs = @param["Attrs"].ToObject<JArray>();
                        string paramString = string.Empty;
                        List<String> attrStrings = new List<String>();
                        foreach (var attr in attrs)
                        {
                            var attrString = attr.ToString();
                            attrStrings.Add(attrString);
                        }

                        if (attrStrings.Count == 1 && attrStrings.Contains("Out"))
                        {
                            //paramString += $"out ";
                        }
                        paramString += $"{paramType} {ReplaceNameIfReservedWord(paramName)}";

                        paramStrings.Add(paramString);
                        paramNames.Add(ReplaceNameIfReservedWord(paramName));
                    }
                }

                AddTabs(indentLevel + 2, ref outputContent);
                string fullParamsString = string.Join(", ", paramStrings);
                outputContent.AppendLine($"protected new function [CallingConvention(.Stdcall)] {returnType}(/*{name}*/SelfOuter* self{(paramStrings.Count > 0 ? ", " : "")}{fullParamsString}) {finalMethodName};");

                var prettyMethod = $"public {returnType} {methodName}({fullParamsString}) mut => VT.[Friend]{finalMethodName}(&this{(paramStrings.Count > 0 ? ", " : "")}{string.Join(", ", paramNames)});";

                prettyMethods.Add(prettyMethod);

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

            AddTabs(indentLevel+1, ref outputContent);
            outputContent.AppendLine($"public const Guid CLSID_{name} = {FormatGuid(guid)};");
            outputContent.AppendLine();
        }

        private static void __ProcessFunctionPtr(JObject comObject, JObject parentType, ref StringBuilder outputContent, int indentLevel)
        {
            var name = comObject["Name"].ToString();

            var returnType = GetTypeFromJObject(comObject["ReturnType"].ToObject<JObject>());

            var @params = comObject["Params"].ToObject<JArray>();

            List<string> paramStrings = new List<string>();

            if (@params != null)
            {
                foreach (var @param in @params)
                {
                    var paramName = @param["Name"].ToString();
                    var paramType = GetTypeFromJObject(@param["Type"].ToObject<JObject>());
                    var attrs = @param["Attrs"].ToObject<JArray>();
                    string paramString = string.Empty;
                    List<String> attrStrings = new List<String>();
                    foreach (var attr in attrs)
                    {
                        var attrString = attr.ToString();
                        attrStrings.Add(attrString);
                    }

                    //if (attrStrings.Count == 1 && attrStrings.Contains("Out"))
                    //{
                    //    paramString += $"out ";
                    //}
                    paramString += $"{paramType} {ReplaceNameIfReservedWord(paramName)}";

                    paramStrings.Add(paramString);
                }
            }

            //AddTabs(indentLevel + 1, ref outputContent);
            outputContent.AppendLine($"public function {returnType} {name}({string.Join(", ", paramStrings)});");
        }

        private static string GetTypeFromJObject(JObject typeObject)
        {
            var typeKind = typeObject["Kind"].ToString();

            if (typeKind == "Native")
            {
                return GetNativeType(typeObject["Name"].ToString());
            }
            else if (typeKind == "NativeTypedef")
            {
                return GetNativeTypedef(typeObject["Name"].ToString());
            }
            else if (typeKind == "ApiRef")
            {
                var targetKind = typeObject["TargetKind"]?.ToString();
                if (targetKind == "Com")
                    return $"{typeObject["Name"].ToString()}*";
                return typeObject["Name"].ToString();
            }
            else if (typeKind == "PointerTo")
            {
                var childObject = typeObject["Child"].ToObject<JObject>();
                var type = GetTypeFromJObject(childObject);
                return $"{type}*";
            }
            else if (typeKind == "Array")
            {
                var childObject = typeObject["Child"].ToObject<JObject>();
                var type = GetTypeFromJObject(childObject);

                int size = 1;

                var shape = typeObject["Shape"].ToObject<JObject>();
                if (shape != null)
                {
                    size = int.Parse(shape["Size"].ToString());
                }


                return $"{type}[{size}]";
            }
            else if (typeKind == "LPArray")
            {
                var childObject = typeObject["Child"].ToObject<JObject>();
                var type = GetTypeFromJObject(childObject);
                return $"{type}*";
            }
            else if (typeKind == "MissingClrType")
            {
                return "void*";
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
                return $"\"{value}\"";
            }

            if (type == "PWSTR")
            {
                return $"(PWSTR)(void*){value}";
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