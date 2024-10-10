using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Compiler;
using Google.Protobuf.Reflection;
using System.Collections;
using System.Reflection;

namespace protoc_gen_turbolink
{
    static class OneofExtenstions
    {
        public static int BlueprintableFieldNumber = 1000;
        public static readonly Extension<OneofOptions, bool> Blueprintable =
            new Extension<OneofOptions, bool>(BlueprintableFieldNumber, FieldCodec.ForBool((uint)BlueprintableFieldNumber));

        public static bool? GetBlueprintable(Object Options, int FieldNumber)
        {
            if (Options == null) return null;
            var unknownFieldsField = Options.GetType().GetField("_unknownFields", BindingFlags.NonPublic | BindingFlags.Instance);
            if (unknownFieldsField == null) return null;
            var unknownFields = unknownFieldsField.GetValue(Options) as UnknownFieldSet;
            if (unknownFields == null) return null;
            var unknownFieldsDictionaryField= unknownFields.GetType().GetField("fields", BindingFlags.NonPublic | BindingFlags.Instance);
            if (unknownFieldsDictionaryField == null) return null;
            object unknownFieldsDictionaryObj = unknownFieldsDictionaryField.GetValue(unknownFields);

            var dictionaryType = unknownFieldsDictionaryObj.GetType();

            if (!typeof(IDictionary).IsAssignableFrom(dictionaryType)) return null;

            foreach (DictionaryEntry field in (IDictionary)unknownFieldsDictionaryObj)
            {
                if ((int)field.Key != FieldNumber)  continue;

                var varintListField = field.Value.GetType().GetField("varintList", BindingFlags.NonPublic | BindingFlags.Instance);
                if (varintListField == null) continue;
                var isBlueprintableList = varintListField.GetValue(field.Value) as List<ulong>;
                if (isBlueprintableList == null) continue;

                foreach (var isBlueprintable in isBlueprintableList)
                {
                    if (isBlueprintable > 0) return true;
                }
                return false;
            }

            return null;
        }
        public static bool? GetBlueprintable(OneofDescriptorProto Desc)
        {
            return GetBlueprintable(Desc.Options, BlueprintableFieldNumber);
        }
    }
    static class MessageExtensions
    {
        static int BlueprintableFieldNumber = 1000;
        public static readonly Extension<MessageOptions, bool> Blueprintable =
            new Extension<MessageOptions, bool>(BlueprintableFieldNumber, FieldCodec.ForBool((uint)BlueprintableFieldNumber));
        public static bool? GetBlueprintable(DescriptorProto Desc)
        {
            return OneofExtenstions.GetBlueprintable(Desc.Options, BlueprintableFieldNumber);
        }
    }
    static class FieldExtenstions
    {
        static int BlueprintableFieldNumber = 1000;
        public static readonly Extension<FieldOptions, bool> Blueprintable =
            new Extension<FieldOptions, bool>(BlueprintableFieldNumber, FieldCodec.ForBool((uint)BlueprintableFieldNumber));
        public static bool? GetBlueprintable(FieldDescriptorProto Desc)
        {
            return OneofExtenstions.GetBlueprintable(Desc.Options, BlueprintableFieldNumber);
        }
    }

    class Program
    {
        static bool GetParam(Dictionary<string, string> paramDictionary, string paramName, bool defaultValue)
		{
            if (!paramDictionary.ContainsKey(paramName))
            {
                return defaultValue;
            }
            string paramValue = paramDictionary[paramName].ToLower();
            if (paramValue != "true" && paramValue != "false")
            {
                return defaultValue;
            }
            return paramValue == "true" ? true : false;
        }
        static void Main(string[] args)
        {
            //read code generator request from stdin.
            CodedInputStream inputStream = new CodedInputStream(Console.OpenStandardInput());
            CodeGeneratorRequest request = new CodeGeneratorRequest();
            request.MergeFrom(inputStream);

            //read request param
            bool dumpRequest = false;
            bool dumpCollection = false;
            bool generateServiceCode = true;
            bool generateJsonCode = false;

            if (request.HasParameter)
			{
                Dictionary<string, string> paramDictionary = request.Parameter
                    .Split(new string[] { ";" }, StringSplitOptions.RemoveEmptyEntries)
                    .GroupBy(param => param.Split('=')[0].Trim(), param => param.Split('=')[1].Trim())
                    .ToDictionary(x => x.Key, x => x.First());

                dumpRequest = GetParam(paramDictionary, "DumpRequest", false);
                dumpCollection = GetParam(paramDictionary, "DumpCollection", false);
                generateServiceCode = GetParam(paramDictionary, "GenerateServiceCode", true);
                generateJsonCode = GetParam(paramDictionary, "GenerateJsonCode", false);
            }

            //create code generator reponse
            CodeGeneratorResponse response = new CodeGeneratorResponse();
            //supported features(optional field)
            response.SupportedFeatures = (ulong)CodeGeneratorResponse.Types.Feature.Proto3Optional;

            //gather and analysis information from all service files
            TurboLinkCollection collection = new TurboLinkCollection();
            string error;
            if (!collection.AnalysisServiceFiles(request, out error))
            {
                //no go!
                response.Error = error;
                WriteResponse(response);
                return;
            }

            foreach (GrpcServiceFile serviceFile in collection.GrpcServiceFiles.Values)
            {
                TurboLinkGenerator generator = new TurboLinkGenerator(serviceFile.ProtoFileDesc, serviceFile);
                generator.BuildOutputFiles(generateServiceCode, generateJsonCode);

                foreach (GeneratedFile generatedFile in generator.GeneratedFiles)
                {
                    CodeGeneratorResponse.Types.File newFile = new CodeGeneratorResponse.Types.File();

                    newFile.Name = generatedFile.FileName;
                    newFile.Content = generatedFile.Content;
                    response.File.Add(newFile);
                }
            }

            //dump input request
            if (dumpRequest)
            {
                CodeGeneratorResponse.Types.File request_file = new CodeGeneratorResponse.Types.File();
                request_file.Name = collection.InputFileNames + "request.json";
                request_file.Content = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
                response.File.Add(request_file);
            }

            //dump service collection
            if (dumpCollection)
            {
                CodeGeneratorResponse.Types.File service_collection = new CodeGeneratorResponse.Types.File();
                service_collection.Name = collection.InputFileNames + "collection.json";
                service_collection.Content = JsonSerializer.Serialize(collection.GrpcServiceFiles, new JsonSerializerOptions { WriteIndented = true });
                response.File.Add(service_collection);
            }
            
            WriteResponse(response);
        }
        private static void WriteResponse(CodeGeneratorResponse response)
		{
            //write response from standard output to grpc
            byte[] data = new byte[response.CalculateSize()];
            Google.Protobuf.CodedOutputStream outputStream = new Google.Protobuf.CodedOutputStream(data);
            response.WriteTo(outputStream);

            Console.OpenStandardOutput().Write(data, 0, response.CalculateSize());
        }
    }
}
