using Microsoft.Build.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Task = Microsoft.Build.Utilities.Task;

namespace Imlinka.Build;

/// <summary>
/// MSBuild task that rewrites compiled assemblies to add Imlinka tracing scopes.
/// </summary>
public sealed class ImlinkaWeaveTask : Task
{
    private const string TraceAttributeFullName = "Imlinka.TraceAttribute";
    private const string TracedAttributeFullName = "Imlinka.TracedAttribute";
    private const string RuntimeFullName = "Imlinka.ProjectTracingRuntime";

    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    public ITaskItem[] References { get; set; } = [];

    public ITaskItem[] SearchPaths { get; set; } = [];

    public string? IntermediateDirectory { get; set; }

    public override bool Execute()
    {
        if (string.IsNullOrWhiteSpace(AssemblyPath) ||
            !File.Exists(AssemblyPath))
        {
            Log.LogMessage(MessageImportance.Low, "Imlinka weaving skipped: assembly was not found.");

            return true;
        }

        try
        {
            var resolver = CreateResolver();
            var pdbPath = Path.ChangeExtension(AssemblyPath, ".pdb");
            var readSymbols = File.Exists(pdbPath);

            var readerParameters = new ReaderParameters
            {
                ReadSymbols = readSymbols,
                ReadWrite = true,
                AssemblyResolver = resolver
            };

            using var assembly = AssemblyDefinition.ReadAssembly(AssemblyPath, readerParameters);

            if (!ReferencesImlinka(assembly))
            {
                Log.LogMessage(MessageImportance.Low, "Imlinka weaving skipped: assembly does not reference Imlinka.");

                return true;
            }

            if (IsSigned(assembly))
            {
                Log.LogWarning(
                    "Imlinka weaving skipped signed assembly '{0}' because re-signing is not supported yet.",
                    AssemblyPath);

                return true;
            }

            var context = WeavingContext.Create(assembly.MainModule);
            var count = 0;

            foreach (var type in assembly.MainModule.Types.SelectMany(EnumerateTypes))
            {
                if (ShouldSkipType(type))
                    continue;

                var tracedType = GetInheritedTracingAttribute(type, TracedAttributeFullName) ??
                                 GetInterfaceTracingAttribute(type, TracedAttributeFullName);
                var spanNamePrefix = GetStringConstructorArgument(tracedType);

                foreach (var method in type.Methods.ToArray())
                {
                    if (!CanWeave(method))
                        continue;

                    var traceMethod = GetTracingAttribute(method, TraceAttributeFullName) ??
                                      GetInterfaceMethodTracingAttribute(type, method, TraceAttributeFullName);
                    var tracedByAttribute = traceMethod is not null || tracedType is not null;

                    if (!tracedByAttribute &&
                        !method.IsPublic)
                        continue;

                    var spanName = GetStringConstructorArgument(traceMethod);
                    WeaveMethod(context, type, method, spanName, spanNamePrefix, tracedByAttribute);
                    count++;
                }
            }

            if (count == 0)
            {
                Log.LogMessage(MessageImportance.Low, "Imlinka weaving completed: no methods were changed.");

                return true;
            }

            assembly.Write(new WriterParameters { WriteSymbols = readSymbols });
            Log.LogMessage(MessageImportance.High, "Imlinka weaving completed: {0} method(s) changed in {1}.", count, AssemblyPath);

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);

            return false;
        }
    }

    private DefaultAssemblyResolver CreateResolver()
    {
        var resolver = new DefaultAssemblyResolver();
        AddSearchDirectory(resolver, Path.GetDirectoryName(AssemblyPath));
        AddSearchDirectory(resolver, IntermediateDirectory);

        foreach (var item in SearchPaths)
            AddSearchDirectory(resolver, item.ItemSpec);

        foreach (var reference in References)
        {
            var path = reference.ItemSpec;
            AddSearchDirectory(resolver, Path.GetDirectoryName(path));
        }

        return resolver;
    }

    private static void AddSearchDirectory(DefaultAssemblyResolver resolver, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Directory.Exists(path))
            return;

        resolver.AddSearchDirectory(path);
    }

    private static bool ReferencesImlinka(AssemblyDefinition assembly) =>
        assembly.Name.Name == "Imlinka"
        || assembly.MainModule.AssemblyReferences.Any(r => r.Name == "Imlinka");

    private static bool IsSigned(AssemblyDefinition assembly) =>
        assembly.Name.HasPublicKey ||
        assembly.Name.PublicKeyToken is { Length: > 0 } ||
        assembly.Name.PublicKey is { Length: > 0 };

    private static IEnumerable<TypeDefinition> EnumerateTypes(TypeDefinition type)
    {
        yield return type;

        foreach (var nested in type.NestedTypes.SelectMany(EnumerateTypes))
            yield return nested;
    }

    private static bool ShouldSkipType(TypeDefinition type) =>
        type.IsInterface
        || type.IsEnum
        || type.IsValueType
        || HasCompilerGeneratedAttribute(type);

    private static bool CanWeave(MethodDefinition method) =>
        method is { HasBody: true, IsConstructor: false }
        && !method.IsAbstract
        && !method.IsPInvokeImpl
        && !method.ReturnType.IsByReference
        && !method.IsGetter
        && !method.IsSetter
        && !method.IsAddOn
        && !method.IsRemoveOn
        && !HasCompilerGeneratedAttribute(method)
        && !method.Body.Instructions.Any(IsAlreadyWovenCall);

    private static bool IsAlreadyWovenCall(Instruction instruction) =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is MethodReference { DeclaringType.FullName: RuntimeFullName, Name: "StartScope" };

    private static bool HasCompilerGeneratedAttribute(ICustomAttributeProvider provider) =>
        provider.HasCustomAttributes
        && provider.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    private static CustomAttribute? GetTracingAttribute(ICustomAttributeProvider provider, string fullName) =>
        provider.HasCustomAttributes ? provider.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == fullName) : null;

    private static CustomAttribute? GetInheritedTracingAttribute(TypeDefinition type, string fullName)
    {
        var attribute = GetTracingAttribute(type, fullName);

        if (attribute is not null)
            return attribute;

        var baseType = type.BaseType;

        while (baseType is not null)
        {
            TypeDefinition resolved;

            try
            {
                resolved = baseType.Resolve();
            }
            catch
            {
                return null;
            }

            attribute = GetTracingAttribute(resolved, fullName);

            if (attribute is not null)
                return attribute;

            baseType = resolved.BaseType;
        }

        return null;
    }

    private static CustomAttribute? GetInterfaceTracingAttribute(TypeDefinition type, string fullName)
    {
        foreach (var interfaceCandidate in EnumerateInterfaces(type))
        {
            var attribute = GetTracingAttribute(interfaceCandidate.Definition, fullName);
            if (attribute is not null)
                return attribute;
        }

        return null;
    }

    private static CustomAttribute? GetInterfaceMethodTracingAttribute(TypeDefinition type, MethodDefinition method, string fullName)
    {
        foreach (var overrideMethod in method.Overrides)
        {
            MethodDefinition? resolvedOverride;
            try
            {
                resolvedOverride = overrideMethod.Resolve();
            }
            catch
            {
                continue;
            }

            var attribute = GetTracingAttribute(resolvedOverride, fullName);
            if (attribute is not null)
                return attribute;
        }

        foreach (var interfaceCandidate in EnumerateInterfaces(type))
        {
            foreach (var interfaceMethod in interfaceCandidate.Definition.Methods)
            {
                if (!MethodMatches(interfaceMethod, method, interfaceCandidate.Reference))
                    continue;

                var attribute = GetTracingAttribute(interfaceMethod, fullName);
                if (attribute is not null)
                    return attribute;
            }
        }

        return null;
    }

    private static IEnumerable<InterfaceCandidate> EnumerateInterfaces(TypeDefinition type)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var interfaceImplementation in type.Interfaces)
        {
            foreach (var interfaceType in EnumerateInterface(interfaceImplementation.InterfaceType, visited))
                yield return interfaceType;
        }
    }

    private static IEnumerable<InterfaceCandidate> EnumerateInterface(TypeReference interfaceReference, ISet<string> visited)
    {
        TypeDefinition interfaceType;
        try
        {
            interfaceType = interfaceReference.Resolve();
        }
        catch
        {
            yield break;
        }

        if (!visited.Add(interfaceReference.FullName))
            yield break;

        yield return new InterfaceCandidate(interfaceReference, interfaceType);

        foreach (var inherited in interfaceType.Interfaces)
        {
            var inheritedReference = ResolveInterfaceTypeReference(inherited.InterfaceType, interfaceReference);

            foreach (var nested in EnumerateInterface(inheritedReference, visited))
                yield return nested;
        }
    }

    private static bool MethodMatches(
        MethodDefinition interfaceMethod,
        MethodDefinition implementationMethod,
        TypeReference interfaceReference)
    {
        if (interfaceMethod.Name != implementationMethod.Name)
            return false;

        if (interfaceMethod.Parameters.Count != implementationMethod.Parameters.Count)
            return false;

        if (!TypeMatches(interfaceMethod.ReturnType, implementationMethod.ReturnType, interfaceReference))
            return false;

        for (var index = 0; index < interfaceMethod.Parameters.Count; index++)
        {
            if (!TypeMatches(interfaceMethod.Parameters[index].ParameterType, implementationMethod.Parameters[index].ParameterType, interfaceReference))
                return false;
        }

        return true;
    }

    private static bool TypeMatches(TypeReference left, TypeReference right, TypeReference interfaceReference)
    {
        var resolvedLeft = ResolveInterfaceTypeReference(left, interfaceReference);

        if (resolvedLeft is GenericInstanceType leftGeneric &&
            right is GenericInstanceType rightGeneric)
        {
            if (leftGeneric.ElementType.FullName != rightGeneric.ElementType.FullName ||
                leftGeneric.GenericArguments.Count != rightGeneric.GenericArguments.Count)
                return false;

            for (var index = 0; index < leftGeneric.GenericArguments.Count; index++)
            {
                if (!TypeMatches(leftGeneric.GenericArguments[index], rightGeneric.GenericArguments[index], interfaceReference))
                    return false;
            }

            return true;
        }

        if (resolvedLeft is ArrayType leftArray &&
            right is ArrayType rightArray)
        {
            return leftArray.Rank == rightArray.Rank &&
                   TypeMatches(leftArray.ElementType, rightArray.ElementType, interfaceReference);
        }

        if (resolvedLeft is ByReferenceType leftByReference &&
            right is ByReferenceType rightByReference)
            return TypeMatches(leftByReference.ElementType, rightByReference.ElementType, interfaceReference);

        if (resolvedLeft is PointerType leftPointer &&
            right is PointerType rightPointer)
            return TypeMatches(leftPointer.ElementType, rightPointer.ElementType, interfaceReference);

        return resolvedLeft.FullName == right.FullName;
    }

    private static TypeReference ResolveInterfaceTypeReference(TypeReference type, TypeReference interfaceReference)
    {
        if (type is GenericParameter genericParameter &&
            genericParameter.Owner is TypeReference &&
            interfaceReference is GenericInstanceType genericInterface)
        {
            var position = genericParameter.Position;
            if (position >= 0 &&
                position < genericInterface.GenericArguments.Count)
                return genericInterface.GenericArguments[position];
        }

        if (type is GenericInstanceType genericType)
        {
            var resolvedGenericType = new GenericInstanceType(genericType.ElementType);

            foreach (var argument in genericType.GenericArguments)
                resolvedGenericType.GenericArguments.Add(ResolveInterfaceTypeReference(argument, interfaceReference));

            return resolvedGenericType;
        }

        if (type is ArrayType arrayType)
            return new ArrayType(ResolveInterfaceTypeReference(arrayType.ElementType, interfaceReference), arrayType.Rank);

        if (type is ByReferenceType byReferenceType)
            return new ByReferenceType(ResolveInterfaceTypeReference(byReferenceType.ElementType, interfaceReference));

        if (type is PointerType pointerType)
            return new PointerType(ResolveInterfaceTypeReference(pointerType.ElementType, interfaceReference));

        return type;
    }

    private static string? GetStringConstructorArgument(CustomAttribute? attribute)
    {
        if (attribute is null ||
            attribute.ConstructorArguments.Count == 0)
            return null;

        return attribute.ConstructorArguments[0].Value as string;
    }

    private static void WeaveMethod(
        WeavingContext context,
        TypeDefinition declaringType,
        MethodDefinition method,
        string? spanName,
        string? spanNamePrefix,
        bool tracedByAttribute)
    {
        var body = method.Body;
        body.InitLocals = true;

        var il = body.GetILProcessor();
        var originalFirst = body.Instructions.First();
        var scopeVariable = new VariableDefinition(context.DisposableType);
        var exceptionVariable = new VariableDefinition(context.ExceptionType);
        body.Variables.Add(scopeVariable);
        body.Variables.Add(exceptionVariable);

        VariableDefinition? returnVariable = null;

        if (method.ReturnType.MetadataType != MetadataType.Void)
        {
            returnVariable = new VariableDefinition(method.ReturnType);
            body.Variables.Add(returnVariable);
        }

        var catchStart = Instruction.Create(OpCodes.Stloc, exceptionVariable);
        var end = Instruction.Create(OpCodes.Nop);
        var finalReturn = Instruction.Create(OpCodes.Ret);

        foreach (var ret in body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
        {
            if (returnVariable is not null)
                il.InsertBefore(ret, Instruction.Create(OpCodes.Stloc, returnVariable));

            ret.OpCode = OpCodes.Leave;
            ret.Operand = end;
        }

        il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldtoken, declaringType));
        il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Call, context.GetTypeFromHandleMethod));
        il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldstr, method.Name));
        InsertNullableString(il, originalFirst, spanName);
        InsertNullableString(il, originalFirst, spanNamePrefix);
        il.InsertBefore(originalFirst, Instruction.Create(tracedByAttribute ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
        il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Call, context.StartScopeMethod));
        il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Stloc, scopeVariable));

        il.Append(catchStart);
        il.Append(Instruction.Create(OpCodes.Ldloc, scopeVariable));
        il.Append(Instruction.Create(OpCodes.Ldloc, exceptionVariable));
        il.Append(Instruction.Create(OpCodes.Call, context.FailScopeMethod));
        il.Append(Instruction.Create(OpCodes.Rethrow));
        il.Append(end);

        if (IsAsyncReturn(method.ReturnType, context, out var completeScopeMethod))
        {
            il.Append(Instruction.Create(OpCodes.Ldloc, returnVariable));
            il.Append(Instruction.Create(OpCodes.Ldloc, scopeVariable));
            il.Append(Instruction.Create(OpCodes.Call, completeScopeMethod));
        }
        else
        {
            il.Append(Instruction.Create(OpCodes.Ldloc, scopeVariable));
            il.Append(Instruction.Create(OpCodes.Callvirt, context.DisposeMethod));

            if (returnVariable is not null)
                il.Append(Instruction.Create(OpCodes.Ldloc, returnVariable));
        }

        il.Append(finalReturn);

        body.ExceptionHandlers.Add(
            new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                TryStart = originalFirst,
                TryEnd = catchStart,
                HandlerStart = catchStart,
                HandlerEnd = end,
                CatchType = context.ExceptionType
            });
    }

    private static bool IsAsyncReturn(
        TypeReference returnType,
        WeavingContext context,
        out MethodReference completeScopeMethod)
    {
        completeScopeMethod = context.CompleteTaskScopeMethod;

        if (returnType.FullName == "System.Threading.Tasks.Task")
            return true;

        if (returnType is GenericInstanceType generic &&
            generic.ElementType.FullName == "System.Threading.Tasks.Task`1")
        {
            completeScopeMethod = CreateGenericMethod(context.CompleteGenericTaskScopeMethod, generic.GenericArguments[0]);

            return true;
        }

        completeScopeMethod = context.CompleteValueTaskScopeMethod;

        if (returnType.FullName == "System.Threading.Tasks.ValueTask")
            return true;

        if (returnType is GenericInstanceType valueTaskGeneric &&
            valueTaskGeneric.ElementType.FullName == "System.Threading.Tasks.ValueTask`1")
        {
            completeScopeMethod = CreateGenericMethod(context.CompleteGenericValueTaskScopeMethod, valueTaskGeneric.GenericArguments[0]);

            return true;
        }

        return false;
    }

    private static GenericInstanceMethod CreateGenericMethod(MethodReference method, TypeReference argument)
    {
        var genericMethod = new GenericInstanceMethod(method);
        genericMethod.GenericArguments.Add(argument);

        return genericMethod;
    }

    private static void InsertNullableString(ILProcessor il, Instruction before, string? value) =>
        il.InsertBefore(before, string.IsNullOrWhiteSpace(value) ? Instruction.Create(OpCodes.Ldnull) : Instruction.Create(OpCodes.Ldstr, value));

    private sealed class WeavingContext
    {
        public TypeReference DisposableType { get; private set; } = null!;

        public TypeReference ExceptionType { get; private set; } = null!;

        public MethodReference DisposeMethod { get; private set; } = null!;

        public MethodReference FailScopeMethod { get; private set; } = null!;

        public MethodReference CompleteTaskScopeMethod { get; private set; } = null!;

        public MethodReference CompleteGenericTaskScopeMethod { get; private set; } = null!;

        public MethodReference CompleteValueTaskScopeMethod { get; private set; } = null!;

        public MethodReference CompleteGenericValueTaskScopeMethod { get; private set; } = null!;

        public MethodReference GetTypeFromHandleMethod { get; private set; } = null!;

        public MethodReference StartScopeMethod { get; private set; } = null!;

        public static WeavingContext Create(ModuleDefinition module)
        {
            var typeType = module.ImportReference(typeof(Type));
            var stringType = module.TypeSystem.String;
            var boolType = module.TypeSystem.Boolean;

            var runtimeType = new TypeReference(
                "Imlinka", "ProjectTracingRuntime", module, module.AssemblyReferences.First(r => r.Name == "Imlinka"));

            var disposableType = module.ImportReference(typeof(IDisposable));
            var exceptionType = module.ImportReference(typeof(Exception));
            var taskType = module.ImportReference(typeof(System.Threading.Tasks.Task));
            var genericTaskType = module.ImportReference(typeof(System.Threading.Tasks.Task<>));
            var valueTaskType = module.ImportReference(typeof(ValueTask));
            var genericValueTaskType = module.ImportReference(typeof(ValueTask<>));

            var completeGenericTaskScopeMethod = new MethodReference("CompleteScope", module.TypeSystem.Void, runtimeType)
            {
                HasThis = false
            };

            var resultParameter = new GenericParameter("TResult", completeGenericTaskScopeMethod);
            var taskOfResult = new GenericInstanceType(genericTaskType);
            taskOfResult.GenericArguments.Add(resultParameter);
            completeGenericTaskScopeMethod.GenericParameters.Add(resultParameter);
            completeGenericTaskScopeMethod.ReturnType = taskOfResult;
            completeGenericTaskScopeMethod.Parameters.Add(new ParameterDefinition(taskOfResult));
            completeGenericTaskScopeMethod.Parameters.Add(new ParameterDefinition(disposableType));

            var completeGenericValueTaskScopeMethod = new MethodReference("CompleteScope", module.TypeSystem.Void, runtimeType)
            {
                HasThis = false
            };

            var valueTaskResultParameter = new GenericParameter("TResult", completeGenericValueTaskScopeMethod);
            var valueTaskOfResult = new GenericInstanceType(genericValueTaskType);
            valueTaskOfResult.GenericArguments.Add(valueTaskResultParameter);
            completeGenericValueTaskScopeMethod.GenericParameters.Add(valueTaskResultParameter);
            completeGenericValueTaskScopeMethod.ReturnType = valueTaskOfResult;
            completeGenericValueTaskScopeMethod.Parameters.Add(new ParameterDefinition(valueTaskOfResult));
            completeGenericValueTaskScopeMethod.Parameters.Add(new ParameterDefinition(disposableType));

            return new WeavingContext
            {
                DisposableType = disposableType,
                ExceptionType = exceptionType,
                DisposeMethod = module.ImportReference(typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!),
                FailScopeMethod = new MethodReference("FailScope", module.TypeSystem.Void, runtimeType)
                {
                    HasThis = false,
                    Parameters =
                    {
                        new ParameterDefinition(disposableType),
                        new ParameterDefinition(exceptionType)
                    }
                },
                CompleteTaskScopeMethod = new MethodReference("CompleteScope", taskType, runtimeType)
                {
                    HasThis = false,
                    Parameters =
                    {
                        new ParameterDefinition(taskType),
                        new ParameterDefinition(disposableType)
                    }
                },
                CompleteGenericTaskScopeMethod = completeGenericTaskScopeMethod,
                CompleteValueTaskScopeMethod = new MethodReference("CompleteScope", valueTaskType, runtimeType)
                {
                    HasThis = false,
                    Parameters =
                    {
                        new ParameterDefinition(valueTaskType),
                        new ParameterDefinition(disposableType)
                    }
                },
                CompleteGenericValueTaskScopeMethod = completeGenericValueTaskScopeMethod,
                GetTypeFromHandleMethod = module.ImportReference(typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!),
                StartScopeMethod = new MethodReference("StartScope", disposableType, runtimeType)
                {
                    HasThis = false,
                    Parameters =
                    {
                        new ParameterDefinition(typeType),
                        new ParameterDefinition(stringType),
                        new ParameterDefinition(stringType),
                        new ParameterDefinition(stringType),
                        new ParameterDefinition(boolType)
                    }
                }
            };
        }
    }

    private sealed class InterfaceCandidate(TypeReference reference, TypeDefinition definition)
    {
        public TypeReference Reference { get; } = reference;

        public TypeDefinition Definition { get; } = definition;
    }
}
