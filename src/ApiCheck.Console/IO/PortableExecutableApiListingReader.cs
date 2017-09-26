// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ApiCheck.Description;

namespace ApiCheck.IO
{
    public class PortableExecutableApiListingReader : IApiListingReader
    {
        private readonly MetadataReader _reader;
        private readonly Stream _fileStream;
        private readonly PEReader _peReader;

        public PortableExecutableApiListingReader(Stream fileStream, bool keepOpen)
        {
            _fileStream = fileStream ?? throw new ArgumentNullException(nameof(fileStream));
            _peReader = new PEReader(fileStream, keepOpen ? PEStreamOptions.LeaveOpen : PEStreamOptions.Default);
            _reader = _peReader.GetMetadataReader(MetadataReaderOptions.Default);
        }

        public ApiListing Read()
        {
            var listing = new ApiListing();

            var def = _reader.GetAssemblyDefinition();
            var name = GetAssemblyName(def.Name, def.Version, def.Culture, def.PublicKey, def.HashAlgorithm, def.Flags);
            listing.AssemblyIdentity = name.ToString();

            foreach (var handle in _reader.TypeDefinitions)
            {
                var definition = _reader.GetTypeDefinition(handle);

                var visibility = definition.Attributes & TypeAttributes.VisibilityMask;
                if (visibility == TypeAttributes.NotPublic)
                {
                    // skip internal types
                    continue;
                }

                var descriptor = GetTypeDescriptor(definition);
                listing.Types.Add(descriptor);

                foreach (var nestedType in definition.GetNestedTypes())
                {
                    var nestedTypeDefinition = _reader.GetTypeDefinition(nestedType);
                    var nestedVisibilty = nestedTypeDefinition.Attributes & TypeAttributes.VisibilityMask;
                    if (nestedVisibilty == TypeAttributes.NotPublic)
                    {
                        // skip internal types
                        continue;
                    }

                    var nestedTypeDescriptor = GetTypeDescriptor(nestedTypeDefinition);
                    listing.Types.Add(nestedTypeDescriptor);
                }
            }

            return listing;
        }

        private TypeDescriptor GetTypeDescriptor(TypeDefinition definition)
        {
            var typeName = _reader.GetString(definition.Name);
            var @namespace = _reader.GetString(definition.Namespace);
            var descriptor = new TypeDescriptor
            {
                Name = @namespace + "." + typeName,
                Abstract = (definition.Attributes & TypeAttributes.Abstract) != 0,
                Sealed = (definition.Attributes & TypeAttributes.Sealed) != 0,
                Visibility = ApiElementVisibility.Public,
            };

            var classSemantics = definition.Attributes & TypeAttributes.ClassSemanticsMask;
            switch (classSemantics)
            {
                case TypeAttributes.Class:
                    var baseTypeName = GetTypeName(definition.BaseType);
                    if (baseTypeName == "System.Enum")
                    {
                        descriptor.Kind = TypeKind.Enumeration;
                    }
                    else if (baseTypeName == "System.ValueType")
                    {
                        descriptor.Kind = TypeKind.Struct;
                    }
                    else
                    {
                        descriptor.BaseType = baseTypeName == "System.Object" ? null : baseTypeName;
                        descriptor.Kind = TypeKind.Class;
                    }
                    break;
                case TypeAttributes.Interface:
                    descriptor.Kind = TypeKind.Interface;
                    break;
            }

            foreach (var genericParamHandle in definition.GetGenericParameters())
            {
                var genericParamDescriptor = GetGenericParameterDescriptor(genericParamHandle);
                descriptor.GenericParameters.Add(genericParamDescriptor);
            }

            foreach (var propHandle in definition.GetProperties())
            {
                var property = GetPropertyDescriptor(propHandle);
                descriptor.Members.Add(property);
            }

            foreach (var methodHandle in definition.GetMethods())
            {
                var method = GetMethodDescriptor(methodHandle);
                descriptor.Members.Add(method);
            }

            foreach (var interfaceHandle in definition.GetInterfaceImplementations())
            {
                var @interface = _reader.GetInterfaceImplementation(interfaceHandle);
                descriptor.ImplementedInterfaces.Add(GetTypeName(@interface.Interface));
            }

            descriptor.Static = descriptor.Kind == TypeKind.Class && descriptor.Sealed && descriptor.Abstract;
            return descriptor;
        }

        private MemberDescriptor GetPropertyDescriptor(PropertyDefinitionHandle handle)
        {
            var property = _reader.GetPropertyDefinition(handle);
            var accessor = property.GetAccessors();
            var getter = _reader.GetMethodDefinition(accessor.Getter);
            MethodDefinition? setter = !accessor.Setter.IsNil ? _reader.GetMethodDefinition(accessor.Setter) : default;

            var descriptor = new MemberDescriptor
            {
                Name = _reader.GetString(property.Name),
                Kind = MemberKind.Field,
                Abstract = (getter.Attributes & MethodAttributes.Abstract) != 0,
                Virtual = (getter.Attributes & MethodAttributes.Virtual) != 0,
                ReadOnly = !setter.HasValue,
                Visibility = (getter.Attributes & MethodAttributes.Public) != 0 ? ApiElementVisibility.Public : ApiElementVisibility.Protected,
            };

            return descriptor;
        }

        private MemberDescriptor GetMethodDescriptor(MethodDefinitionHandle handle)
        {
            var method = _reader.GetMethodDefinition(handle);
            var signature = _reader.GetBlobReader(method.Signature);

            var descriptor = new MemberDescriptor
            {
                Name = _reader.GetString(method.Name),
                Abstract = (method.Attributes & MethodAttributes.Abstract) != 0,
                Static = (method.Attributes & MethodAttributes.Static) != 0,
                New = (method.Attributes & MethodAttributes.NewSlot) != 0,
                Virtual = (method.Attributes & MethodAttributes.Virtual) != 0,
                Sealed = (method.Attributes & MethodAttributes.Final) != 0,
            };

            return descriptor;
        }

        private GenericParameterDescriptor GetGenericParameterDescriptor(GenericParameterHandle handle)
        {
            var genericParam = _reader.GetGenericParameter(handle);
            var descriptor = new GenericParameterDescriptor
            {
                ParameterPosition = genericParam.Index,
                ParameterName = _reader.GetString(genericParam.Name),
                Class = (genericParam.Attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0,
                Struct = (genericParam.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0,
                New = (genericParam.Attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0,
            };
            return descriptor;
        }

        private Dictionary<EntityHandle, string> _typeNameCache = new Dictionary<EntityHandle, string>();

        private string GetTypeName(EntityHandle entityHandle)
        {
            if (_typeNameCache.TryGetValue(entityHandle, out var cachedName))
            {
                return cachedName;
            }

            string typeName;
            switch (entityHandle.Kind)
            {
                case HandleKind.TypeReference:
                    var baseTypeRef = _reader.GetTypeReference((TypeReferenceHandle)entityHandle);
                    typeName = _reader.GetString(baseTypeRef.Namespace) + "." + _reader.GetString(baseTypeRef.Name);
                    break;
                case HandleKind.TypeDefinition:
                    var baseTypeDef = _reader.GetTypeDefinition((TypeDefinitionHandle)entityHandle);
                    var sb = new StringBuilder();
                    sb.Append(_reader.GetString(baseTypeDef.Namespace));
                    sb.Append('.');
                    sb.Append(_reader.GetString(baseTypeDef.Name));
                    var genericParams = baseTypeDef.GetGenericParameters();

                    if (genericParams.Count > 0)
                    {
                        var first = true;
                        foreach (var parameterHandle in genericParams)
                        {
                            if (first)
                            {
                                sb.Append('<');
                                first = false;
                            }
                            else
                            {
                                sb.Append(", ");
                            }
                            var parameter = _reader.GetGenericParameter(parameterHandle);
                            var constraints = parameter.GetConstraints();
                            if (constraints.Count > 0)
                            {
                                foreach (var constraintHandle in constraints)
                                {
                                    var constraint = _reader.GetGenericParameterConstraint(constraintHandle);
                                    sb.Append(GetTypeName(constraint.Type));
                                }
                            }
                            else
                            {
                                sb.Append(_reader.GetString(parameter.Name));
                            }
                        }
                        sb.Append('>');
                    }

                    typeName = sb.ToString();
                    break;
                case HandleKind.TypeSpecification:
                    var entity = FirstEntityHandleProvider.Instance.GetTypeFromSpecification(_reader, (TypeSpecificationHandle)entityHandle);
                    typeName = GetTypeName(entity);
                    break;
                default:
                    throw new ArgumentException("Unexpected base type kind:" + entityHandle.Kind);
            }

            _typeNameCache[entityHandle] = typeName;
            return typeName;
        }

        public void Dispose()
        {
            _peReader.Dispose();
        }

        // TODO in this will be added in new versions of System.Reflection.Metadata
        private AssemblyName GetAssemblyName(StringHandle nameHandle, Version version, StringHandle cultureHandle, BlobHandle publicKeyOrTokenHandle, AssemblyHashAlgorithm assemblyHashAlgorithm, AssemblyFlags flags)
        {
            string name = _reader.GetString(nameHandle);
            string cultureName = (!cultureHandle.IsNil) ? _reader.GetString(cultureHandle) : null;
            var hashAlgorithm = (System.Configuration.Assemblies.AssemblyHashAlgorithm)assemblyHashAlgorithm;
            byte[] publicKeyOrToken = !publicKeyOrTokenHandle.IsNil ? _reader.GetBlobBytes(publicKeyOrTokenHandle) : null;

            var assemblyName = new AssemblyName(name)
            {
                Version = version,
                CultureName = cultureName,
                HashAlgorithm = hashAlgorithm,
                Flags = GetAssemblyNameFlags(flags),
                ContentType = GetContentTypeFromAssemblyFlags(flags)
            };

            bool hasPublicKey = (flags & AssemblyFlags.PublicKey) != 0;
            if (hasPublicKey)
            {
                assemblyName.SetPublicKey(publicKeyOrToken);
            }
            else
            {
                assemblyName.SetPublicKeyToken(publicKeyOrToken);
            }

            return assemblyName;
        }

        private AssemblyNameFlags GetAssemblyNameFlags(AssemblyFlags flags)
        {
            AssemblyNameFlags assemblyNameFlags = AssemblyNameFlags.None;

            if ((flags & AssemblyFlags.PublicKey) != 0)
                assemblyNameFlags |= AssemblyNameFlags.PublicKey;

            if ((flags & AssemblyFlags.Retargetable) != 0)
                assemblyNameFlags |= AssemblyNameFlags.Retargetable;

            if ((flags & AssemblyFlags.EnableJitCompileTracking) != 0)
                assemblyNameFlags |= AssemblyNameFlags.EnableJITcompileTracking;

            if ((flags & AssemblyFlags.DisableJitCompileOptimizer) != 0)
                assemblyNameFlags |= AssemblyNameFlags.EnableJITcompileOptimizer;

            return assemblyNameFlags;
        }

        private AssemblyContentType GetContentTypeFromAssemblyFlags(AssemblyFlags flags)
        {
            return (AssemblyContentType)(((int)flags & (int)AssemblyFlags.ContentTypeMask) >> 9);
        }

        /// <summary>
        /// Used to produce the simple-full-name components of a type from metadata.
        /// The name is 'simple' in that it does not contain things like backticks,
        /// generic arguments, or nested type + separators.  Instead just hte name
        /// of the type, any containing types, and the component parts of its namespace
        /// are added.  For example, for the type "X.Y.O`1.I`2, we will produce [X, Y, O, I]
        /// 
        /// </summary>
        private class FirstEntityHandleProvider : ISignatureTypeProvider<EntityHandle, object>
        {
            public static readonly FirstEntityHandleProvider Instance = new FirstEntityHandleProvider();

            public EntityHandle GetTypeFromSpecification(MetadataReader reader, TypeSpecificationHandle handle)
            {
                // Create a decoder to process the type specification (which happens with
                // instantiated generics).  It will call back into us to get the first handle
                // for the type def or type ref that the specification starts with.
                var sigReader = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
                return new SignatureDecoder<EntityHandle, object>(this, reader, genericContext: null).DecodeType(ref sigReader);
            }

            public EntityHandle GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
                GetTypeFromSpecification(reader, handle);

            public EntityHandle GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => handle;
            public EntityHandle GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => handle;

            // We want the first handle as is, without any handles for the generic args.
            public EntityHandle GetGenericInstantiation(EntityHandle genericType, ImmutableArray<EntityHandle> typeArguments) => genericType;

            // All the signature elements that would normally augment the passed in type will
            // just pass it along unchanged.
            public EntityHandle GetModifiedType(EntityHandle modifier, EntityHandle unmodifiedType, bool isRequired) => unmodifiedType;
            public EntityHandle GetPinnedType(EntityHandle elementType) => elementType;
            public EntityHandle GetArrayType(EntityHandle elementType, ArrayShape shape) => elementType;
            public EntityHandle GetByReferenceType(EntityHandle elementType) => elementType;
            public EntityHandle GetPointerType(EntityHandle elementType) => elementType;
            public EntityHandle GetSZArrayType(EntityHandle elementType) => elementType;

            // We'll never get function pointer types in any types we care about, so we can
            // just return the empty string.  Similarly, as we never construct generics,
            // there is no need to provide anything for the generic parameter names.
            public EntityHandle GetFunctionPointerType(MethodSignature<EntityHandle> signature) => default;
            public EntityHandle GetGenericMethodParameter(object genericContext, int index) => default;
            public EntityHandle GetGenericTypeParameter(object genericContext, int index) => default;

            public EntityHandle GetPrimitiveType(PrimitiveTypeCode typeCode) => default;
        }
    }
}
