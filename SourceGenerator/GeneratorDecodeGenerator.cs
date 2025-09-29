using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace SourceGenerator {
    [Generator]
    public class GeneratorDecodeGenerator : IIncrementalGenerator {
        public void Initialize(IncrementalGeneratorInitializationContext context) {
            // 查找所有标记了[GeneratorSerializable]的类
            var provider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (s, _) => IsSyntaxTargetForGeneration(s),
                    transform: (ctx, _) => GetSemanticTargetForGeneration(ctx))
                .Where(m => m != null);
            // 组合编译信息
            var compilationAndClasses = context.CompilationProvider.Combine(provider.Collect());
            // 注册源生成
            context.RegisterSourceOutput(compilationAndClasses,
                 (spc, source) => Execute(source.Left, source.Right, spc));
        }

        private static bool IsSyntaxTargetForGeneration(SyntaxNode node) {
            return node is ClassDeclarationSyntax classDeclaration
                && classDeclaration.AttributeLists.Count > 0;
        }

        private static ClassDeclarationSyntax GetSemanticTargetForGeneration(GeneratorSyntaxContext context) {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            foreach (AttributeListSyntax attributeList in classDeclaration.AttributeLists) {
                foreach (AttributeSyntax attribute in attributeList.Attributes) {
                    if (context.SemanticModel.GetSymbolInfo(attribute).Symbol is IMethodSymbol attributeSymbol) {
                        INamedTypeSymbol attributeContainingTypeSymbol = attributeSymbol.ContainingType;
                        string fullName = attributeContainingTypeSymbol.ToDisplayString();
                        if (fullName == "RainWorldConnect.Attributes.GeneratorSerializableAttribute") {
                            return classDeclaration;
                        }
                    }
                }
            }
            return null;
        }

        private static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes, SourceProductionContext context) {
            if (classes.IsDefaultOrEmpty) {
                return;
            }
            // 获取属性符号
            var generatorSerializableAttribute = compilation.GetTypeByMetadataName("RainWorldConnect.Attributes.GeneratorSerializableAttribute");
            var serializableMemberAttribute = compilation.GetTypeByMetadataName("RainWorldConnect.Attributes.SerializableMemberAttribute");
            if (generatorSerializableAttribute is null || serializableMemberAttribute is null) {
                return;
            }
            // 处理每个标记了[GeneratorSerializable]的类
            foreach (var classDeclaration in classes) {
                var model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                if (!(model.GetDeclaredSymbol(classDeclaration) is INamedTypeSymbol classSymbol) || !HasAttribute(classSymbol, generatorSerializableAttribute)) {
                    continue;
                }
                // 获取所有标记了[EncodeMember]的成员（字段和属性）及其配置
                var memberFields = GetMemberFields(classSymbol, serializableMemberAttribute);
                // 生成Decode方法
                var source = GenerateDecodeMethod(classSymbol, memberFields, compilation);
                // 添加到源中
                context.AddSource($"{classSymbol.Name}_Decode.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        }

        // 检查类型是否具有指定特性
        private static bool HasAttribute(INamedTypeSymbol classSymbol, INamedTypeSymbol attributeSymbol) {
            return classSymbol.GetAttributes().Any(attr =>
                SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeSymbol));
        }

        // 获取所有标记了[EncodeMember]的成员及其配置
        private static List<MemberInfo> GetMemberFields(INamedTypeSymbol classSymbol, INamedTypeSymbol attributeSymbol) {
            var members = new List<MemberInfo>();
            foreach (var member in classSymbol.GetMembers()) {
                if (member is IFieldSymbol || member is IPropertySymbol) {
                    var encodeAttr = member.GetAttributes().FirstOrDefault(attr =>
                        SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeSymbol));
                    if (encodeAttr != null) {
                        // 获取Index值，从命名参数获取
                        var indexArg = encodeAttr.NamedArguments.FirstOrDefault(arg => arg.Key == "Index");
                        int index = 0;
                        if (!indexArg.Equals(default) && indexArg.Value.Value != null) {
                            index = (int)indexArg.Value.Value;
                        }
                        // 获取SkipNullCheck值，从命名参数获取
                        var skipNullCheckArg = encodeAttr.NamedArguments.FirstOrDefault(arg => arg.Key == "SkipNullCheck");
                        bool skipNullCheck = false;
                        if (!skipNullCheckArg.Equals(default) && skipNullCheckArg.Value.Value != null) {
                            skipNullCheck = (bool)skipNullCheckArg.Value.Value;
                        }
                        members.Add(new MemberInfo {
                            Symbol = member,
                            Index = index,
                            SkipNullCheck = skipNullCheck
                        });
                    }
                }
            }
            return members.OrderBy(m => m.Index).ToList();
        }

        // 生成Decode方法
        private static string GenerateDecodeMethod(INamedTypeSymbol classSymbol, List<MemberInfo> memberInfos, Compilation compilation) {
            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
            var className = classSymbol.Name;
            var sb = new StringBuilder();
            sb.AppendLine("using RainWorldConnect.Network.Base;");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Buffers.Binary;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using TouchSocket.Core;");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    // 源生成器版本 1.0.2.9");
            sb.AppendLine($"    partial class {className}");
            sb.AppendLine("    {");
            sb.AppendLine("        public override void Decode<TReader>(ref TReader reader, ParseState parseState)");
            sb.AppendLine("        {");
            sb.AppendLine("            while (parseState.state >= 0)");
            sb.AppendLine("            {");
            sb.AppendLine("                switch (parseState.state)");
            sb.AppendLine("                {");
            // 处理每个成员
            int currentState = 0;
            int totalMembers = memberInfos.Count;
            for (int i = 0; i < totalMembers; i++) {
                var memberInfo = memberInfos[i];
                var symbol = memberInfo.Symbol;
                string memberName = symbol.Name;
                ITypeSymbol memberType = GetMemberType(symbol);
                bool skipNullCheck = memberInfo.SkipNullCheck;
                bool isLastMember = i == totalMembers - 1;
                if (memberType == null) {
                    continue;
                }
                sb.AppendLine($"                    case {currentState}: // {memberName}");
                sb.AppendLine("                    {");
                if (IsFixedSizePrimitiveType(memberType)) {
                    // 基本类型
                    GeneratePrimitiveDecodeCode(sb, memberName, memberType, currentState, isLastMember);
                    currentState++;
                } else if (memberType.SpecialType == SpecialType.System_String) {
                    // 字符串
                    GenerateStringDecodeCode(sb, memberName, skipNullCheck, currentState, isLastMember);
                    currentState += 2; // 需要两个状态：长度和内容
                } else if (memberType is IArrayTypeSymbol arrayType) {
                    // 数组
                    GenerateArrayDecodeCode(sb, memberName, arrayType, skipNullCheck, compilation, currentState, ref currentState, isLastMember);
                } else {
                    // 自定义类型
                    if (skipNullCheck) {
                        sb.AppendLine($"                        // 跳过null检查，直接解码");
                        sb.AppendLine($"                        if (parseState.next == null)");
                        sb.AppendLine($"                            parseState.next = new ParseState();");
                        sb.AppendLine($"                        {memberName}.Decode(ref reader, ref parseState.next);");
                        sb.AppendLine($"                        if (parseState.next.state == -1)");
                        sb.AppendLine($"                        {{");
                        sb.AppendLine($"                            parseState.next = null;");
                        sb.AppendLine($"                            parseState.state = {(isLastMember ? -1 : currentState + 1)};");
                        sb.AppendLine($"                        }}");
                        sb.AppendLine($"                        else if (parseState.next.state == -2)");
                        sb.AppendLine($"                        {{");
                        sb.AppendLine($"                            parseState.state = -2;");
                        sb.AppendLine($"                            return;");
                        sb.AppendLine($"                        }}");
                        sb.AppendLine($"                        else");
                        sb.AppendLine($"                        {{");
                        sb.AppendLine($"                            return;");
                        sb.AppendLine($"                        }}");
                    } else {
                        sb.AppendLine($"                        // 需要null检查");
                        sb.AppendLine($"                        if (parseState.next == null)");
                        sb.AppendLine($"                        {{");
                        sb.AppendLine($"                            // 第一次进入此状态，需要检查是否为null");
                        sb.AppendLine($"                            if (reader.BytesRemaining < 1) return;");
                        sb.AppendLine($"                            byte isNotNull = reader.GetSpan(1)[0];");
                        sb.AppendLine($"                            reader.Advance(1);");
                        sb.AppendLine($"                            if (isNotNull == 0)");
                        sb.AppendLine($"                            {{");
                        sb.AppendLine($"                                {memberName} = null;");
                        sb.AppendLine($"                                parseState.state = {(isLastMember ? -1 : currentState + 1)};");
                        sb.AppendLine($"                            }}");
                        sb.AppendLine($"                            else");
                        sb.AppendLine($"                            {{");
                        sb.AppendLine($"                                if ({memberName} == null)");
                        sb.AppendLine($"                                    {memberName} = new {memberType.ToDisplayString()}();");
                        sb.AppendLine($"                                parseState.next = new ParseState();");
                        sb.AppendLine($"                                // 继续处理当前状态，不改变状态值");
                        sb.AppendLine($"                            }}");
                        sb.AppendLine($"                        }}");
                        sb.AppendLine($"                        else");
                        sb.AppendLine($"                        {{");
                        sb.AppendLine($"                            // 已经检查过null标记，继续解码");
                        sb.AppendLine($"                            {memberName}.Decode(ref reader, parseState.next);");
                        sb.AppendLine($"                            if (parseState.next.state == -1)");
                        sb.AppendLine($"                            {{");
                        sb.AppendLine($"                                parseState.next = null;");
                        sb.AppendLine($"                                parseState.state = {(isLastMember ? -1 : currentState + 1)};");
                        sb.AppendLine($"                            }}");
                        sb.AppendLine($"                            else if (parseState.next.state == -2)");
                        sb.AppendLine($"                            {{");
                        sb.AppendLine($"                                parseState.state = -2;");
                        sb.AppendLine($"                                return;");
                        sb.AppendLine($"                            }}");
                        sb.AppendLine($"                            else");
                        sb.AppendLine($"                            {{");
                        sb.AppendLine($"                                return;");
                        sb.AppendLine($"                            }}");
                        sb.AppendLine($"                        }}");
                    }
                    currentState++;
                }
                sb.AppendLine("                    }");
                sb.AppendLine("                    break;");
            }
            // 默认情况处理未知状态
            sb.AppendLine("                    default:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        // 错误状态");
            sb.AppendLine("                        parseState.state = -2;");
            sb.AppendLine("                        return;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // 获取成员类型
        private static ITypeSymbol GetMemberType(ISymbol symbol) {
            switch (symbol) {
                case IFieldSymbol field:
                    return field.Type;
                case IPropertySymbol property:
                    return property.Type;
                default:
                    return null;
            }
        }

        // 判断是否为固定大小的基本类型
        private static bool IsFixedSizePrimitiveType(ITypeSymbol type) {
            switch (type.SpecialType) {
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Boolean:
                case SpecialType.System_Char:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                    return true;
                default:
                    return false;
            }
        }

        // 生成基本类型解码代码
        private static void GeneratePrimitiveDecodeCode(StringBuilder sb, string memberName, ITypeSymbol memberType, int state, bool isLastMember) {
            if (memberType.SpecialType == SpecialType.System_Boolean ||
                memberType.SpecialType == SpecialType.System_Byte ||
                memberType.SpecialType == SpecialType.System_SByte) {
                // 对于1字节类型，使用GetSpan(1)[0]和Advance(1)
                sb.AppendLine($"                        if (reader.BytesRemaining < 1) return;");
                switch (memberType.SpecialType) {
                    case SpecialType.System_Boolean:
                        sb.AppendLine($"                        {memberName} = reader.GetSpan(1)[0] != 0;");
                        break;
                    case SpecialType.System_Byte:
                        sb.AppendLine($"                        {memberName} = reader.GetSpan(1)[0];");
                        break;
                    case SpecialType.System_SByte:
                        sb.AppendLine($"                        {memberName} = (sbyte)reader.GetSpan(1)[0];");
                        break;
                }
                sb.AppendLine($"                        reader.Advance(1);");
            } else {
                // 对于其他固定大小类型，使用原有的BinaryPrimitives方法
                sb.AppendLine($"                        if (reader.BytesRemaining < {GetPrimitiveTypeSize(memberType)}) return;");
                switch (memberType.SpecialType) {
                    case SpecialType.System_Int32:
                        sb.AppendLine($"                        {memberName} = BinaryPrimitives.ReadInt32LittleEndian(reader.GetSpan(4));");
                        sb.AppendLine($"                        reader.Advance(4);");
                        break;
                    case SpecialType.System_UInt32:
                        sb.AppendLine($"                        {memberName} = BinaryPrimitives.ReadUInt32LittleEndian(reader.GetSpan(4));");
                        sb.AppendLine($"                        reader.Advance(4);");
                        break;
                    case SpecialType.System_Int16:
                        sb.AppendLine($"                        {memberName} = BinaryPrimitives.ReadInt16LittleEndian(reader.GetSpan(2));");
                        sb.AppendLine($"                        reader.Advance(2);");
                        break;
                    case SpecialType.System_UInt16:
                        sb.AppendLine($"                        {memberName} = BinaryPrimitives.ReadUInt16LittleEndian(reader.GetSpan(2));");
                        sb.AppendLine($"                        reader.Advance(2);");
                        break;
                    case SpecialType.System_Int64:
                        sb.AppendLine($"                        {memberName} = BinaryPrimitives.ReadInt64LittleEndian(reader.GetSpan(8));");
                        sb.AppendLine($"                        reader.Advance(8);");
                        break;
                    case SpecialType.System_UInt64:
                        sb.AppendLine($"                        {memberName} = BinaryPrimitives.ReadUInt64LittleEndian(reader.GetSpan(8));");
                        sb.AppendLine($"                        reader.Advance(8);");
                        break;
                    case SpecialType.System_Single:
                        sb.AppendLine($"                        {memberName} = BinaryPrimitives.ReadSingleLittleEndian(reader.GetSpan(4));");
                        sb.AppendLine($"                        reader.Advance(4);");
                        break;
                    case SpecialType.System_Double:
                        sb.AppendLine($"                        {memberName} = BinaryPrimitives.ReadDoubleLittleEndian(reader.GetSpan(8));");
                        sb.AppendLine($"                        reader.Advance(8);");
                        break;
                    case SpecialType.System_Char:
                        sb.AppendLine($"                        {memberName} = (char)BinaryPrimitives.ReadUInt16LittleEndian(reader.GetSpan(2));");
                        sb.AppendLine($"                        reader.Advance(2);");
                        break;
                }
            }
            sb.AppendLine($"                        parseState.state = {(isLastMember ? -1 : state + 1)};");
        }

        // 生成字符串解码代码
        private static void GenerateStringDecodeCode(StringBuilder sb, string memberName, bool skipNullCheck, int state, bool isLastMember) {
            // 状态1: 读取长度
            sb.AppendLine($"                        if (reader.BytesRemaining < 4) return;");
            sb.AppendLine($"                        int strLength = BinaryPrimitives.ReadInt32LittleEndian(reader.GetSpan(4));");
            sb.AppendLine($"                        reader.Advance(4);");

            if (skipNullCheck) {
                sb.AppendLine($"                        parseState.strLength = strLength; // 存储长度");
                sb.AppendLine($"                        parseState.state = {state + 1}; // 移动到内容读取状态");
            } else {
                sb.AppendLine($"                        if (strLength == -1)");
                sb.AppendLine("                        {");
                sb.AppendLine($"                            {memberName} = null;");
                sb.AppendLine($"                            parseState.state = {(isLastMember ? -1 : state + 2)}; // 移动到下一个字段或完成");
                sb.AppendLine("                        }");
                sb.AppendLine("                        else");
                sb.AppendLine("                        {");
                sb.AppendLine($"                            parseState.strLength = strLength; // 存储长度");
                sb.AppendLine($"                            parseState.state = {state + 1}; // 移动到内容读取状态");
                sb.AppendLine("                        }");
            }
            sb.AppendLine("                    }");
            sb.AppendLine("                    break;");

            // 状态2: 读取内容
            sb.AppendLine($"                    case {state + 1}: // {memberName} (读取内容)");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        int strLength = parseState.strLength; // 获取存储的长度");
            sb.AppendLine($"                        if (reader.BytesRemaining < strLength) return;");
            sb.AppendLine($"                        {memberName} = Encoding.UTF8.GetString(reader.GetSpan(strLength));");
            sb.AppendLine($"                        reader.Advance(strLength);");
            sb.AppendLine($"                        parseState.state = {(isLastMember ? -1 : state + 2)}; // 移动到下一个字段或完成");
            // 这里不需要break，函数退出后主体有额外添加的break
        }

        // 生成数组解码代码
        private static void GenerateArrayDecodeCode(StringBuilder sb, string memberName, IArrayTypeSymbol arrayType, bool skipNullCheck, Compilation compilation, int startState, ref int nextState, bool isLastMember) {
            ITypeSymbol elementType = arrayType.ElementType;
            string elementTypeName = elementType.ToDisplayString();
            int arrayContentState = startState + 1;
            nextState = startState + 2;

            // 状态1: 读取数组长度
            sb.AppendLine($"                        if (reader.BytesRemaining < 4) return;");
            if (skipNullCheck) {
                sb.AppendLine($"                        int arrayLength = BinaryPrimitives.ReadInt32LittleEndian(reader.GetSpan(4));");
                sb.AppendLine($"                        reader.Advance(4);");
                sb.AppendLine($"                        {memberName} = new {elementTypeName}[arrayLength];");
                sb.AppendLine($"                        parseState.state = {arrayContentState}; // 移动到数组内容读取状态");
                sb.AppendLine($"                        parseState.arrayIndex = 0; // 重置数组索引");
                if (IsFixedSizePrimitiveType(elementType)) {
                } else if (elementType.SpecialType == SpecialType.System_String) {
                    sb.AppendLine($"                        parseState.strLength = -1; // 重置字符串数组的字符串长度");
                } else {
                    sb.AppendLine($"                        parseState.next = null; // 重置自定义类型递归状态");
                }
            } else {
                sb.AppendLine($"                        int arrayLength = BinaryPrimitives.ReadInt32LittleEndian(reader.GetSpan(4));");
                sb.AppendLine($"                        reader.Advance(4);");
                sb.AppendLine($"                        if (arrayLength != -1)");
                sb.AppendLine("                        {");
                sb.AppendLine($"                            {memberName} = new {elementTypeName}[arrayLength];");
                sb.AppendLine($"                            parseState.state = {arrayContentState}; // 移动到数组内容读取状态");
                sb.AppendLine($"                            parseState.arrayIndex = 0; // 重置数组索引");
                if (IsFixedSizePrimitiveType(elementType)) {
                } else if (elementType.SpecialType == SpecialType.System_String) {
                    sb.AppendLine($"                            parseState.strLength = -1; // 重置字符串数组的字符串长度");
                } else {
                    sb.AppendLine($"                            parseState.next = null; // 重置自定义类型递归状态");
                }
                sb.AppendLine("                        }");
                sb.AppendLine("                        else");
                sb.AppendLine("                        {");
                sb.AppendLine($"                            {memberName} = null;");
                sb.AppendLine($"                            parseState.state = {(isLastMember ? -1 : nextState)}; // 移动到下一个字段");
                sb.AppendLine("                        }");
            }
            sb.AppendLine("                    }");
            sb.AppendLine("                    break;");

            // 状态2: 读取数组内容
            sb.AppendLine($"                    case {arrayContentState}: // {memberName} (数组内容)");
            sb.AppendLine("                    {");

            if (IsFixedSizePrimitiveType(elementType)) {
                // 基本类型数组的处理逻辑保持不变
                int elementSize = GetPrimitiveTypeSize(elementType);
                sb.AppendLine($"                        if (reader.BytesRemaining < {memberName}.Length * {elementSize}) return;");
                sb.AppendLine($"                        var byteArray = MemoryMarshal.Cast<byte, {elementTypeName}>(reader.GetSpan({memberName}.Length * {elementSize}));");
                sb.AppendLine($"                        byteArray.CopyTo({memberName});");
                sb.AppendLine($"                        reader.Advance({memberName}.Length * {elementSize});");
                sb.AppendLine($"                        parseState.state = {(isLastMember ? -1 : nextState)}; // 移动到下一个字段");
            } else if (elementType.SpecialType == SpecialType.System_String) {
                // 字符串数组的特殊处理
                sb.AppendLine($"                        while (parseState.arrayIndex < {memberName}.Length)");
                sb.AppendLine("                        {");
                sb.AppendLine($"                            // 检查当前元素的状态: -1表示需要读取长度, >=0表示需要读取内容");
                sb.AppendLine($"                            if (parseState.strLength == -1)");
                sb.AppendLine("                            {");
                sb.AppendLine($"                                // 需要读取字符串长度");
                sb.AppendLine($"                                if (reader.BytesRemaining < 4) return;");
                sb.AppendLine($"                                int elementLength = BinaryPrimitives.ReadInt32LittleEndian(reader.GetSpan(4));");
                sb.AppendLine($"                                reader.Advance(4);");
                sb.AppendLine($"                                parseState.strLength = elementLength; // 存储长度");
                if (!skipNullCheck) {
                    sb.AppendLine($"                                if (elementLength == -1)");
                    sb.AppendLine("                                {");
                    sb.AppendLine($"                                    // 长度为-1表示null");
                    sb.AppendLine($"                                    {memberName}[parseState.arrayIndex] = null;");
                    sb.AppendLine($"                                    parseState.arrayIndex++;");
                    sb.AppendLine($"                                    parseState.strLength = -1; // 重置为-1，准备读取下一个元素的长度");
                    sb.AppendLine($"                                    continue;");
                    sb.AppendLine("                                }");
                }
                sb.AppendLine("                            }");
                sb.AppendLine($"                            else");
                sb.AppendLine("                            {");
                sb.AppendLine($"                                // 需要读取字符串内容");
                sb.AppendLine($"                                int elementLength = parseState.strLength;");
                sb.AppendLine($"                                if (reader.BytesRemaining < elementLength) return;");
                sb.AppendLine($"                                {memberName}[parseState.arrayIndex] = Encoding.UTF8.GetString(reader.GetSpan(elementLength));");
                sb.AppendLine($"                                reader.Advance(elementLength);");
                sb.AppendLine($"                                parseState.arrayIndex++;");
                sb.AppendLine($"                                parseState.strLength = -1; // 重置为-1，准备读取下一个元素的长度");
                sb.AppendLine("                            }");
                sb.AppendLine("                        }");
                sb.AppendLine($"                        parseState.state = {(isLastMember ? -1 : nextState)}; // 移动到下一个字段");
            } else {
                // 自定义类型数组的处理逻辑保持不变
                sb.AppendLine($"                        while (parseState.arrayIndex < {memberName}.Length)");
                sb.AppendLine("                        {");
                sb.AppendLine($"                            if (parseState.next == null)");
                sb.AppendLine($"                            {{");
                if (skipNullCheck) {
                    sb.AppendLine($"                                {memberName}[parseState.arrayIndex] = new {elementTypeName}();");
                    sb.AppendLine($"                                parseState.next = new ParseState();");
                } else {
                    sb.AppendLine($"                                // 需要检查当前元素是否为null");
                    sb.AppendLine($"                                if (reader.BytesRemaining < 1) return;");
                    sb.AppendLine($"                                byte isElementNotNull = reader.GetSpan(1)[0];");
                    sb.AppendLine($"                                reader.Advance(1);");
                    sb.AppendLine($"                                if (isElementNotNull == 0)");
                    sb.AppendLine($"                                {{");
                    sb.AppendLine($"                                    parseState.arrayIndex++;");
                    sb.AppendLine($"                                    continue; // 处理下一个元素");
                    sb.AppendLine($"                                }}");
                    sb.AppendLine($"                                else");
                    sb.AppendLine($"                                {{");
                    sb.AppendLine($"                                    {memberName}[parseState.arrayIndex] = new {elementTypeName}();");
                    sb.AppendLine($"                                    parseState.next = new ParseState();");
                    sb.AppendLine($"                                }}");
                }
                sb.AppendLine($"                            }}");
                sb.AppendLine($"                            // 解码当前元素");
                sb.AppendLine($"                            {memberName}[parseState.arrayIndex].Decode(ref reader, parseState.next);");
                sb.AppendLine($"                            if (parseState.next.state == -1)");
                sb.AppendLine($"                            {{");
                sb.AppendLine($"                                parseState.next = null;");
                sb.AppendLine($"                                parseState.arrayIndex++;");
                sb.AppendLine($"                            }}");
                sb.AppendLine($"                            else if (parseState.next.state == -2)");
                sb.AppendLine($"                            {{");
                sb.AppendLine($"                                parseState.state = -2;");
                sb.AppendLine($"                                return;");
                sb.AppendLine($"                            }}");
                sb.AppendLine($"                            else");
                sb.AppendLine($"                            {{");
                sb.AppendLine($"                                return;");
                sb.AppendLine($"                            }}");
                sb.AppendLine("                        }");
                sb.AppendLine($"                        parseState.state = {(isLastMember ? -1 : nextState)}; // 移动到下一个字段");
            }
            // 这里不需要break，函数退出后主体有额外添加的break
        }

        // 获取基本类型的大小
        private static int GetPrimitiveTypeSize(ITypeSymbol type) {
            switch (type.SpecialType) {
                case SpecialType.System_Int32:
                    return 4;
                case SpecialType.System_UInt32:
                    return 4;
                case SpecialType.System_Int16:
                    return 2;
                case SpecialType.System_UInt16:
                    return 2;
                case SpecialType.System_Int64:
                    return 8;
                case SpecialType.System_UInt64:
                    return 8;
                case SpecialType.System_Single:
                    return 4;
                case SpecialType.System_Double:
                    return 8;
                case SpecialType.System_Boolean:
                    return 1;
                case SpecialType.System_Char:
                    return 2;
                case SpecialType.System_Byte:
                    return 1;
                case SpecialType.System_SByte:
                    return 1;
                default:
                    return 0;
            }
        }

        // 辅助类，用于存储成员信息
        private class MemberInfo {
            public ISymbol Symbol {
                get; set;
            }
            public int Index {
                get; set;
            }
            public bool SkipNullCheck {
                get; set;
            }
        }
    }
}