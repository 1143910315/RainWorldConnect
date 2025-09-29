using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace SourceGenerator {
    [Generator]
    public class GeneratorEncodeGenerator : IIncrementalGenerator {
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

                // 生成Encode方法
                var source = GenerateEncodeMethod(classSymbol, memberFields, compilation);

                // 添加到源中
                context.AddSource($"{classSymbol.Name}_Encode.g.cs", SourceText.From(source, Encoding.UTF8));
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

        // 生成Encode方法
        private static string GenerateEncodeMethod(INamedTypeSymbol classSymbol, List<MemberInfo> memberInfos, Compilation compilation) {
            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
            var className = classSymbol.Name;

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Buffers.Binary;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using TouchSocket.Core;");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    // 源生成器版本 1.0.1.6");
            sb.AppendLine($"    partial class {className}");
            sb.AppendLine("    {");
            sb.AppendLine("        public override void Encode<TWriter>(ref TWriter writer)");
            sb.AppendLine("        {");

            // 分组处理连续的字段
            var groups = GroupFields(memberInfos, compilation);
            int bufferIndex = 0;

            foreach (var group in groups) {
                if (group.Members.Count == 1) {
                    // 处理单个成员
                    var memberInfo = group.Members[0];
                    var symbol = memberInfo.Symbol;
                    string memberName = symbol.Name;
                    string memberAccess = $"{memberName}";
                    ITypeSymbol memberType = GetMemberType(symbol);
                    bool skipNullCheck = memberInfo.SkipNullCheck;

                    if (memberType == null) {
                        continue;
                    }

                    if (IsFixedSizePrimitiveType(memberType)) {
                        // 单个基本类型
                        GenerateSinglePrimitiveEncodeCode(sb, memberAccess, memberType);
                    } else if (memberType.SpecialType == SpecialType.System_String) {
                        // 单个字符串
                        GenerateStringEncodeCode(sb, memberAccess, skipNullCheck, ref bufferIndex);
                    } else if (memberType is IArrayTypeSymbol arrayType) {
                        // 单个数组
                        GenerateArrayEncodeCode(sb, memberAccess, arrayType, skipNullCheck, compilation, ref bufferIndex);
                    } else {
                        // 自定义类型
                        if (skipNullCheck) {
                            sb.AppendLine($"            {memberAccess}.Encode(ref writer);");
                        } else {
                            sb.AppendLine($"            if ({memberAccess} != null)");
                            sb.AppendLine("            {");
                            sb.AppendLine($"                writer.GetSpan(1)[0] = 1;"); // 非null标记
                            sb.AppendLine($"                writer.Advance(1);");
                            sb.AppendLine($"                {memberAccess}.Encode(ref writer);");
                            sb.AppendLine("            }");
                            sb.AppendLine("            else");
                            sb.AppendLine("            {");
                            sb.AppendLine($"                writer.GetSpan(1)[0] = 0;"); // null标记
                            sb.AppendLine($"                writer.Advance(1);");
                            sb.AppendLine("            }");
                        }
                    }
                } else {
                    // 处理多个成员的组
                    GenerateGroupEncodeCode(sb, group, compilation, ref bufferIndex);
                }
            }

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
            ;
        }

        // 将成员分组为连续的字段
        private static List<MemberGroup> GroupFields(List<MemberInfo> memberInfos, Compilation compilation) {
            var groups = new List<MemberGroup>();
            var currentGroup = new MemberGroup();

            foreach (var memberInfo in memberInfos) {
                var member = memberInfo.Symbol;
                var memberType = GetMemberType(member);

                // 将基本类型、字符串和基本类型数组分为一组
                if (memberType != null &&
                    (IsFixedSizePrimitiveType(memberType) ||
                     memberType.SpecialType == SpecialType.System_String ||
                     (memberType is IArrayTypeSymbol arrayType && IsFixedSizePrimitiveType(arrayType.ElementType)))) {
                    currentGroup.Members.Add(memberInfo);

                    // 计算固定大小
                    if (IsFixedSizePrimitiveType(memberType)) {
                        currentGroup.FixedSize += GetPrimitiveTypeSize(memberType);
                    } else if (memberType.SpecialType == SpecialType.System_String) {
                        // 字符串有4字节的长度字段
                        currentGroup.FixedSize += 4;
                    } else if (memberType is IArrayTypeSymbol) {
                        // 数组有4字节的长度字段
                        currentGroup.FixedSize += 4;
                    }
                } else {
                    // 非基本类型，结束当前组（如果有）并创建新组
                    if (currentGroup.Members.Count > 0) {
                        groups.Add(currentGroup);
                        currentGroup = new MemberGroup();
                    }
                    currentGroup.Members.Add(memberInfo);
                }
            }

            // 添加最后一个组
            if (currentGroup.Members.Count > 0) {
                groups.Add(currentGroup);
            }

            return groups;
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

        // 生成单个基本类型成员的编码代码
        private static void GenerateSinglePrimitiveEncodeCode(StringBuilder sb, string memberAccess, ITypeSymbol memberType) {
            switch (memberType.SpecialType) {
                case SpecialType.System_Int32:
                    sb.AppendLine($"            BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), {memberAccess});");
                    sb.AppendLine($"            writer.Advance(4);");
                    break;
                case SpecialType.System_UInt32:
                    sb.AppendLine($"            BinaryPrimitives.WriteUInt32LittleEndian(writer.GetSpan(4), {memberAccess});");
                    sb.AppendLine($"            writer.Advance(4);");
                    break;
                case SpecialType.System_Int16:
                    sb.AppendLine($"            BinaryPrimitives.WriteInt16LittleEndian(writer.GetSpan(2), {memberAccess});");
                    sb.AppendLine($"            writer.Advance(2);");
                    break;
                case SpecialType.System_UInt16:
                    sb.AppendLine($"            BinaryPrimitives.WriteUInt16LittleEndian(writer.GetSpan(2), {memberAccess});");
                    sb.AppendLine($"            writer.Advance(2);");
                    break;
                case SpecialType.System_Int64:
                    sb.AppendLine($"            BinaryPrimitives.WriteInt64LittleEndian(writer.GetSpan(8), {memberAccess});");
                    sb.AppendLine($"            writer.Advance(8);");
                    break;
                case SpecialType.System_UInt64:
                    sb.AppendLine($"            BinaryPrimitives.WriteUInt64LittleEndian(writer.GetSpan(8), {memberAccess});");
                    sb.AppendLine($"            writer.Advance(8);");
                    break;
                case SpecialType.System_Single:
                    sb.AppendLine($"            BinaryPrimitives.WriteSingleLittleEndian(writer.GetSpan(4), {memberAccess});");
                    sb.AppendLine($"            writer.Advance(4);");
                    break;
                case SpecialType.System_Double:
                    sb.AppendLine($"            BinaryPrimitives.WriteDoubleLittleEndian(writer.GetSpan(8), {memberAccess});");
                    sb.AppendLine($"            writer.Advance(8);");
                    break;
                case SpecialType.System_Boolean:
                    sb.AppendLine($"            writer.GetSpan(1)[0] = {memberAccess} ? (byte)1 : (byte)0;");
                    sb.AppendLine($"            writer.Advance(1);");
                    break;
                case SpecialType.System_Char:
                    sb.AppendLine($"            BinaryPrimitives.WriteUInt16LittleEndian(writer.GetSpan(2), {memberAccess});");
                    sb.AppendLine($"            writer.Advance(2);");
                    break;
                case SpecialType.System_Byte:
                    sb.AppendLine($"            writer.GetSpan(1)[0] = {memberAccess};");
                    sb.AppendLine($"            writer.Advance(1);");
                    break;
                case SpecialType.System_SByte:
                    sb.AppendLine($"            writer.GetSpan(1)[0] = (byte){memberAccess};");
                    sb.AppendLine($"            writer.Advance(1);");
                    break;
            }
        }

        // 生成组的编码代码
        private static void GenerateGroupEncodeCode(StringBuilder sb, MemberGroup group, Compilation compilation, ref int bufferIndex) {
            // 计算总大小
            sb.AppendLine($"            int totalSize{bufferIndex} = {group.FixedSize};");

            // 声明临时变量来存储字符串长度
            int stringVarIndex = 0;
            foreach (var memberInfo in group.Members) {
                var member = memberInfo.Symbol;
                var memberType = GetMemberType(member);
                if (memberType.SpecialType == SpecialType.System_String) {
                    string memberName = member.Name;
                    bool skipNullCheck = memberInfo.SkipNullCheck;

                    if (skipNullCheck) {
                        sb.AppendLine($"            int strLen{bufferIndex}_{stringVarIndex} = Encoding.UTF8.GetByteCount({memberName});");
                        sb.AppendLine($"            totalSize{bufferIndex} += strLen{bufferIndex}_{stringVarIndex};");
                    } else {
                        sb.AppendLine($"            int strLen{bufferIndex}_{stringVarIndex} = -1;");
                        sb.AppendLine($"            if ({memberName} != null)");
                        sb.AppendLine("            {");
                        sb.AppendLine($"                strLen{bufferIndex}_{stringVarIndex} = Encoding.UTF8.GetByteCount({memberName});");
                        sb.AppendLine($"                totalSize{bufferIndex} += strLen{bufferIndex}_{stringVarIndex};");
                        sb.AppendLine("            }");
                    }
                    stringVarIndex++;
                }
            }

            // 添加字符串长度到总大小
            stringVarIndex = 0;
            // 为可变大小成员生成大小计算代码
            foreach (var memberInfo in group.Members) {
                var member = memberInfo.Symbol;
                var memberType = GetMemberType(member);

                if (memberType is IArrayTypeSymbol arrayType && IsFixedSizePrimitiveType(arrayType.ElementType)) {
                    // 基本类型数组
                    string memberName = member.Name;
                    bool skipNullCheck = memberInfo.SkipNullCheck;
                    int elementSize = GetPrimitiveTypeSize(arrayType.ElementType);

                    if (skipNullCheck) {
                        sb.AppendLine($"            totalSize{bufferIndex} += {memberName}.Length * {elementSize};");
                    } else {
                        // 注意：FixedSize已经包含了4字节的长度字段，这里只需要计算实际元素的大小
                        sb.AppendLine($"            if ({memberName} != null)");
                        sb.AppendLine($"                totalSize{bufferIndex} += {memberName}.Length * {elementSize};");
                    }
                }
            }

            // 分配缓冲区
            sb.AppendLine($"            byte[] buffer{bufferIndex} = new byte[totalSize{bufferIndex}];");
            sb.AppendLine($"            int offset{bufferIndex} = 0;");

            // 写入数据到缓冲区
            foreach (var memberInfo in group.Members) {
                var member = memberInfo.Symbol;
                var memberType = GetMemberType(member);
                string memberName = member.Name;
                string memberAccess = $"{memberName}";
                bool skipNullCheck = memberInfo.SkipNullCheck;

                if (IsFixedSizePrimitiveType(memberType)) {
                    // 基本类型
                    switch (memberType.SpecialType) {
                        case SpecialType.System_Int32:
                            sb.AppendLine($"            BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess});");
                            sb.AppendLine($"            offset{bufferIndex} += 4;");
                            break;
                        case SpecialType.System_UInt32:
                            sb.AppendLine($"            BinaryPrimitives.WriteUInt32LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess});");
                            sb.AppendLine($"            offset{bufferIndex} += 4;");
                            break;
                        case SpecialType.System_Int16:
                            sb.AppendLine($"            BinaryPrimitives.WriteInt16LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess});");
                            sb.AppendLine($"            offset{bufferIndex} += 2;");
                            break;
                        case SpecialType.System_UInt16:
                            sb.AppendLine($"            BinaryPrimitives.WriteUInt16LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess});");
                            sb.AppendLine($"            offset{bufferIndex} += 2;");
                            break;
                        case SpecialType.System_Int64:
                            sb.AppendLine($"            BinaryPrimitives.WriteInt64LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess});");
                            sb.AppendLine($"            offset{bufferIndex} += 8;");
                            break;
                        case SpecialType.System_UInt64:
                            sb.AppendLine($"            BinaryPrimitives.WriteUInt64LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess});");
                            sb.AppendLine($"            offset{bufferIndex} += 8;");
                            break;
                        case SpecialType.System_Single:
                            sb.AppendLine($"            BinaryPrimitives.WriteSingleLittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess});");
                            sb.AppendLine($"            offset{bufferIndex} += 4;");
                            break;
                        case SpecialType.System_Double:
                            sb.AppendLine($"            BinaryPrimitives.WriteDoubleLittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess});");
                            sb.AppendLine($"            offset{bufferIndex} += 8;");
                            break;
                        case SpecialType.System_Boolean:
                            sb.AppendLine($"            buffer{bufferIndex}[offset{bufferIndex}] = {memberAccess} ? (byte)1 : (byte)0;");
                            sb.AppendLine($"            offset{bufferIndex} += 1;");
                            break;
                        case SpecialType.System_Char:
                            sb.AppendLine($"            BinaryPrimitives.WriteUInt16LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess});");
                            sb.AppendLine($"            offset{bufferIndex} += 2;");
                            break;
                        case SpecialType.System_Byte:
                            sb.AppendLine($"            buffer{bufferIndex}[offset{bufferIndex}] = {memberAccess};");
                            sb.AppendLine($"            offset{bufferIndex} += 1;");
                            break;
                        case SpecialType.System_SByte:
                            sb.AppendLine($"            buffer{bufferIndex}[offset{bufferIndex}] = (byte){memberAccess};");
                            sb.AppendLine($"            offset{bufferIndex} += 1;");
                            break;
                    }
                } else if (memberType.SpecialType == SpecialType.System_String) {
                    // 字符串
                    if (skipNullCheck) {
                        sb.AppendLine($"            BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), strLen{bufferIndex}_{stringVarIndex});");
                        sb.AppendLine($"            offset{bufferIndex} += 4;");
                        sb.AppendLine($"            Encoding.UTF8.GetBytes({memberAccess}, 0, {memberAccess}.Length, buffer{bufferIndex}, offset{bufferIndex});");
                        sb.AppendLine($"            offset{bufferIndex} += strLen{bufferIndex}_{stringVarIndex};");
                    } else {
                        sb.AppendLine($"            BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), strLen{bufferIndex}_{stringVarIndex});");
                        sb.AppendLine($"            offset{bufferIndex} += 4;");
                        sb.AppendLine($"            if ({memberAccess} != null)");
                        sb.AppendLine("            {");
                        sb.AppendLine($"                Encoding.UTF8.GetBytes({memberAccess}, 0, {memberAccess}.Length, buffer{bufferIndex}, offset{bufferIndex});");
                        sb.AppendLine($"                offset{bufferIndex} += strLen{bufferIndex}_{stringVarIndex};");
                        sb.AppendLine("            }");
                    }
                    stringVarIndex++;
                } else if (memberType is IArrayTypeSymbol arrayType && IsFixedSizePrimitiveType(arrayType.ElementType)) {
                    // 基本类型数组
                    string elementTypeName = arrayType.ElementType.ToDisplayString();
                    int elementSize = GetPrimitiveTypeSize(arrayType.ElementType);

                    if (skipNullCheck) {
                        sb.AppendLine($"            BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess}.Length);");
                        sb.AppendLine($"            offset{bufferIndex} += 4;");
                        sb.AppendLine($"            var byteArray{bufferIndex} = MemoryMarshal.Cast<{elementTypeName}, byte>({memberAccess}.AsSpan());");
                        sb.AppendLine($"            byteArray{bufferIndex}.CopyTo(buffer{bufferIndex}.AsSpan(offset{bufferIndex}));");
                        sb.AppendLine($"            offset{bufferIndex} += {memberAccess}.Length * {elementSize};");
                    } else {
                        sb.AppendLine($"            if ({memberAccess} == null)");
                        sb.AppendLine("            {");
                        sb.AppendLine($"                BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), -1);");
                        sb.AppendLine($"                offset{bufferIndex} += 4;");
                        sb.AppendLine("            }");
                        sb.AppendLine("            else");
                        sb.AppendLine("            {");
                        sb.AppendLine($"                BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(offset{bufferIndex}), {memberAccess}.Length);");
                        sb.AppendLine($"                offset{bufferIndex} += 4;");
                        sb.AppendLine($"                var byteArray{bufferIndex} = MemoryMarshal.Cast<{elementTypeName}, byte>({memberAccess}.AsSpan());");
                        sb.AppendLine($"                byteArray{bufferIndex}.CopyTo(buffer{bufferIndex}.AsSpan(offset{bufferIndex}));");
                        sb.AppendLine($"                offset{bufferIndex} += {memberAccess}.Length * {elementSize};");
                        sb.AppendLine("            }");
                    }
                }
            }

            // 写入缓冲区到writer
            sb.AppendLine($"            writer.Write(buffer{bufferIndex}.AsSpan(0, totalSize{bufferIndex}));");

            bufferIndex++;
        }

        // 生成字符串编码代码
        private static void GenerateStringEncodeCode(StringBuilder sb, string memberAccess, bool skipNullCheck, ref int bufferIndex) {
            if (skipNullCheck) {
                sb.AppendLine($"            int strLength{bufferIndex} = Encoding.UTF8.GetByteCount({memberAccess});");
                sb.AppendLine($"            byte[] buffer{bufferIndex} = new byte[4 + strLength{bufferIndex}];");
                sb.AppendLine($"            BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(0, 4), strLength{bufferIndex});");
                sb.AppendLine($"            Encoding.UTF8.GetBytes({memberAccess}, 0, {memberAccess}.Length, buffer{bufferIndex}, 4);");
                sb.AppendLine($"            writer.Write(buffer{bufferIndex});");
            } else {
                sb.AppendLine($"            if ({memberAccess} == null)");
                sb.AppendLine("            {");
                sb.AppendLine($"                byte[] buffer{bufferIndex} = new byte[4];");
                sb.AppendLine($"                BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}, -1);");
                sb.AppendLine($"                writer.Write(buffer{bufferIndex});");
                sb.AppendLine("            }");
                sb.AppendLine("            else");
                sb.AppendLine("            {");
                sb.AppendLine($"                int strLength{bufferIndex} = Encoding.UTF8.GetByteCount({memberAccess});");
                sb.AppendLine($"                byte[] buffer{bufferIndex} = new byte[4 + strLength{bufferIndex}];");
                sb.AppendLine($"                BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(0, 4), strLength{bufferIndex});");
                sb.AppendLine($"                Encoding.UTF8.GetBytes({memberAccess}, 0, {memberAccess}.Length, buffer{bufferIndex}, 4);");
                sb.AppendLine($"                writer.Write(buffer{bufferIndex});");
                sb.AppendLine("            }");
            }
            bufferIndex++;
        }

        // 生成数组编码代码
        private static void GenerateArrayEncodeCode(StringBuilder sb, string memberAccess, IArrayTypeSymbol arrayType, bool skipNullCheck, Compilation compilation, ref int bufferIndex) {
            ITypeSymbol elementType = arrayType.ElementType;
            string elementTypeName = elementType.ToDisplayString();

            if (skipNullCheck) {
                // 处理基本类型数组
                if (IsFixedSizePrimitiveType(elementType)) {
                    int elementSize = GetPrimitiveTypeSize(elementType);
                    sb.AppendLine($"                int arrayLength = {memberAccess}.Length;");
                    sb.AppendLine($"                byte[] buffer{bufferIndex} = new byte[4 + arrayLength * {elementSize}];");
                    sb.AppendLine($"                BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(0, 4), arrayLength);");

                    // 使用MemoryMarshal.Cast将数组转换为字节序列
                    sb.AppendLine($"                var byteArray{bufferIndex} = MemoryMarshal.Cast<{elementTypeName}, byte>({memberAccess}.AsSpan());");
                    sb.AppendLine($"                byteArray{bufferIndex}.CopyTo(buffer{bufferIndex}.AsSpan(4));");

                    sb.AppendLine($"                writer.Write(buffer{bufferIndex});");
                } else if (elementType.SpecialType == SpecialType.System_String) {
                    // 字符串数组
                    sb.AppendLine($"                BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), {memberAccess}.Length);");
                    sb.AppendLine($"                writer.Advance(4);");
                    sb.AppendLine($"                for (int i = 0; i < {memberAccess}.Length; i++)");
                    sb.AppendLine($"                {{");
                    sb.AppendLine($"                    int strLength{bufferIndex} = Encoding.UTF8.GetByteCount({memberAccess}[i]);");
                    sb.AppendLine($"                    byte[] buffer{bufferIndex} = new byte[4 + strLength{bufferIndex}];");
                    sb.AppendLine($"                    BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(0, 4), strLength{bufferIndex});");
                    sb.AppendLine($"                    Encoding.UTF8.GetBytes({memberAccess}[i], 0, {memberAccess}[i].Length, buffer{bufferIndex}, 4);");
                    sb.AppendLine($"                    writer.Write(buffer{bufferIndex});");
                    sb.AppendLine($"                }}");
                } else {
                    // 自定义类型数组，SkipNullCheck为true表示数组和元素都不为null
                    sb.AppendLine($"            BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), {memberAccess}.Length);");
                    sb.AppendLine("            writer.Advance(4);");
                    sb.AppendLine($"            for (int i = 0; i < {memberAccess}.Length; i++)");
                    sb.AppendLine($"            {{");
                    sb.AppendLine($"                {memberAccess}[i].Encode(ref writer);");
                    sb.AppendLine($"            }}");
                }
            } else {
                sb.AppendLine($"            if ({memberAccess} == null)");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                byte[] buffer{bufferIndex} = new byte[4];");
                sb.AppendLine($"                BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}, -1);");
                sb.AppendLine($"                writer.Write(buffer{bufferIndex});");
                sb.AppendLine($"            }}");
                sb.AppendLine($"            else");
                sb.AppendLine($"            {{");

                // 处理基本类型数组
                if (IsFixedSizePrimitiveType(elementType)) {
                    int elementSize = GetPrimitiveTypeSize(elementType);
                    sb.AppendLine($"                int arrayLength = {memberAccess}.Length;");
                    sb.AppendLine($"                byte[] buffer{bufferIndex} = new byte[4 + arrayLength * {elementSize}];");
                    sb.AppendLine($"                BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(0, 4), arrayLength);");

                    // 使用MemoryMarshal.Cast将数组转换为字节序列
                    sb.AppendLine($"                var byteArray{bufferIndex} = MemoryMarshal.Cast<{elementTypeName}, byte>({memberAccess}.AsSpan());");
                    sb.AppendLine($"                byteArray{bufferIndex}.CopyTo(buffer{bufferIndex}.AsSpan(4));");

                    sb.AppendLine($"                writer.Write(buffer{bufferIndex});");
                } else if (elementType.SpecialType == SpecialType.System_String) {
                    // 字符串数组
                    sb.AppendLine($"                BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), {memberAccess}.Length);");
                    sb.AppendLine("                writer.Advance(4);");
                    sb.AppendLine($"                for (int i = 0; i < {memberAccess}.Length; i++)");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    if ({memberAccess}[i] == null)");
                    sb.AppendLine("                    {");
                    sb.AppendLine($"                        byte[] buffer{bufferIndex} = new byte[4];");
                    sb.AppendLine($"                        BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}, -1);");
                    sb.AppendLine($"                        writer.Write(buffer{bufferIndex});");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                    else");
                    sb.AppendLine("                    {");
                    sb.AppendLine($"                        int strLength{bufferIndex} = Encoding.UTF8.GetByteCount({memberAccess}[i]);");
                    sb.AppendLine($"                        byte[] buffer{bufferIndex} = new byte[4 + strLength{bufferIndex}];");
                    sb.AppendLine($"                        BinaryPrimitives.WriteInt32LittleEndian(buffer{bufferIndex}.AsSpan(0, 4), strLength{bufferIndex});");
                    sb.AppendLine($"                        Encoding.UTF8.GetBytes({memberAccess}[i], 0, {memberAccess}[i].Length, buffer{bufferIndex}, 4);");
                    sb.AppendLine($"                        writer.Write(buffer{bufferIndex});");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                }");
                } else {
                    // 自定义类型数组，SkipNullCheck为false表示数组和元素都可能为null
                    sb.AppendLine($"                BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), {memberAccess}.Length);");
                    sb.AppendLine("                writer.Advance(4);");
                    sb.AppendLine($"                for (int i = 0; i < {memberAccess}.Length; i++)");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    if ({memberAccess}[i] != null)");
                    sb.AppendLine("                    {");
                    sb.AppendLine($"                        writer.GetSpan(1)[0] = 1;"); // 非null标记
                    sb.AppendLine($"                        writer.Advance(1);");
                    sb.AppendLine($"                        {memberAccess}[i].Encode(ref writer);");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                    else");
                    sb.AppendLine("                    {");
                    sb.AppendLine($"                        writer.GetSpan(1)[0] = 0;"); // null标记
                    sb.AppendLine($"                        writer.Advance(1);");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                }");
                }

                sb.AppendLine("            }");
            }
            bufferIndex++;
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

        // 辅助类，用于分组成员
        private class MemberGroup {
            public List<MemberInfo> Members { get; set; } = new List<MemberInfo>();
            public int FixedSize { get; set; } = 0; // 存储组内基本类型成员的固定大小总和
        }
    }
}