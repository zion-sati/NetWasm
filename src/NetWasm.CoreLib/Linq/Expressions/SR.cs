// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.Linq.Expressions
{
    internal static class SR
    {
        internal const string AccessorsCannotHaveByRefArgs =
            "Accessor indexes cannot be passed ByRef.";
        internal const string AmbiguousMatchInExpandoObject =
            "More than one key matching '{0}' was found in the ExpandoObject.";
        internal const string ArgCntMustBeGreaterThanNameCnt =
            "Argument count must be greater than number of named arguments.";
        internal const string ArgumentCannotBeOfTypeVoid =
            "Argument type cannot be System.Void.";
        internal const string ArgumentMemberNotDeclOnType =
            " The member '{0}' is not declared on type '{1}' being created";
        internal const string ArgumentMustBeArray =
            "Argument must be array";
        internal const string ArgumentMustBeArrayIndexType =
            "Argument for array index must be of type Int32";
        internal const string ArgumentMustBeBoolean =
            "Argument must be boolean";
        internal const string ArgumentMustBeFieldInfoOrPropertyInfo =
            "Argument must be either a FieldInfo or PropertyInfo";
        internal const string ArgumentMustBeFieldInfoOrPropertyInfoOrMethod =
            "Argument must be either a FieldInfo, PropertyInfo or MethodInfo";
        internal const string ArgumentMustBeInstanceMember =
            "Argument must be an instance member";
        internal const string ArgumentMustBeInteger =
            "Argument must be of an integer type";
        internal const string ArgumentMustBeSingleDimensionalArrayType =
            "Argument must be single-dimensional, zero-based array type";
        internal const string ArgumentTypeDoesNotMatchMember =
            " Argument type '{0}' does not match the corresponding member type '{1}'";
        internal const string ArgumentTypesMustMatch =
            "Argument types do not match";
        internal const string BinaryOperatorNotDefined =
            "The binary operator {0} is not defined for the types '{1}' and '{2}'.";
        internal const string BothAccessorsMustBeStatic =
            "Both accessors must be static.";
        internal const string BoundsCannotBeLessThanOne =
            "Bounds count cannot be less than 1";
        internal const string CoalesceUsedOnNonNullType =
            "Coalesce used with type that cannot be null";
        internal const string CollectionModifiedWhileEnumerating =
            "Collection was modified; enumeration operation may not execute.";
        internal const string CollectionReadOnly =
            "Collection is read-only.";
        internal const string DuplicateVariable =
            "Found duplicate parameter '{0}'. Each ParameterExpression in the list must be a unique object.";
        internal const string ElementInitializerMethodNoRefOutParam =
            "Parameter '{0}' of element initializer method '{1}' must not be a pass by reference parameter";
        internal const string ElementInitializerMethodNotAdd =
            "Element initializer method must be named 'Add'";
        internal const string ElementInitializerMethodStatic =
            "Element initializer method must be an instance method";
        internal const string ElementInitializerMethodWithZeroArgs =
            "Element initializer method must have at least 1 parameter";
        internal const string EnumerationIsDone =
            "Enumeration has either not started or has already finished.";
        internal const string ExpressionMustBeReadable =
            "Expression must be readable";
        internal const string ExpressionTypeCannotInitializeArrayType =
            "An expression of type '{0}' cannot be used to initialize an array of type '{1}'";
        internal const string ExpressionTypeDoesNotMatchConstructorParameter =
            "Expression of type '{0}' cannot be used for constructor parameter of type '{1}'";
        internal const string ExpressionTypeDoesNotMatchMethodParameter =
            "Expression of type '{0}' cannot be used for parameter of type '{1}' of method '{2}'";
        internal const string ExpressionTypeDoesNotMatchParameter =
            "Expression of type '{0}' cannot be used for parameter of type '{1}'";
        internal const string ExpressionTypeDoesNotMatchReturn =
            "Expression of type '{0}' cannot be used for return type '{1}'";
        internal const string ExtensionNodeMustOverrideProperty =
            "Extension node must override the property {0}.";
        internal const string IncorrectNumberOfArgumentsForMembers =
            "Incorrect number of arguments for the given members ";
        internal const string IncorrectNumberOfConstructorArguments =
            "Incorrect number of arguments for constructor";
        internal const string IncorrectNumberOfIndexes =
            "Incorrect number of indexes";
        internal const string IncorrectNumberOfLambdaArguments =
            "Incorrect number of arguments supplied for lambda invocation";
        internal const string IncorrectNumberOfLambdaDeclarationParameters =
            "Incorrect number of parameters supplied for lambda declaration";
        internal const string IncorrectNumberOfMembersForGivenConstructor =
            " Incorrect number of members for constructor";
        internal const string IncorrectNumberOfMethodCallArguments =
            "Incorrect number of arguments supplied for call to method '{0}'";
        internal const string IncorrectTypeForTypeAs =
            "The type used in TypeAs Expression must be of reference or nullable type, {0} is neither";
        internal const string IndexesOfSetGetMustMatch =
            "Indexing parameters of getter and setter must match.";
        internal const string InstanceAndMethodTypeMismatch =
            "Method '{0}' declared on type '{1}' cannot be called with instance of type '{2}'";
        internal const string InvalidArgumentValue_ParamName =
            "Invalid argument value";
        internal const string InvalidNullValue =
            "The value null is not of type '{0}' and cannot be used in this collection.";
        internal const string InvalidObjectType =
            "The value '{0}' is not of type '{1}' and cannot be used in this collection.";
        internal const string InvalidUnboxType =
            "Can only unbox from an object or interface type to a value type.";
        internal const string KeyDoesNotExistInExpando =
            "The specified key '{0}' does not exist in the ExpandoObject.";
        internal const string LambdaTypeMustBeDerivedFromSystemDelegate =
            "Lambda type parameter must be derived from System.MulticastDelegate";
        internal const string LogicalOperatorMustHaveBooleanOperators =
            "The user-defined operator method '{1}' for operator '{0}' must have associated boolean True and False operators.";
        internal const string MethodContainsGenericParameters =
            "Method {0} contains generic parameters";
        internal const string MethodIsGeneric =
            "Method {0} is a generic method definition";
        internal const string MethodNotPropertyAccessor =
            "The method '{0}' is not a property accessor";
        internal const string NonEmptyCollectionRequired =
            "Non-empty collection required";
        internal const string NonStaticConstructorRequired =
            "The constructor should not be static";
        internal const string NotAMemberOfAnyType =
            "'{0}' is not a member of any type";
        internal const string NotAMemberOfType =
            "'{0}' is not a member of type '{1}'";
        internal const string OnlyStaticPropertiesHaveNullInstance =
            "Static property requires null instance, non-static property requires non-null instance.";
        internal const string OperandTypesDoNotMatchParameters =
            "The operands for operator '{0}' do not match the parameters of method '{1}'.";
        internal const string ParameterExpressionNotValidAsDelegate =
            "ParameterExpression of type '{0}' cannot be used for delegate parameter of type '{1}'";
        internal const string PropertyCannotHaveRefType =
            "Property cannot have a managed pointer type.";
        internal const string PropertyDoesNotHaveAccessor =
            "The property '{0}' has no 'get' or 'set' accessors";
        internal const string PropertyDoesNotHaveGetter =
            "The property '{0}' has no 'get' accessor";
        internal const string PropertyDoesNotHaveSetter =
            "The property '{0}' has no 'set' accessor";
        internal const string PropertyTypeCannotBeVoid =
            "Property cannot have a void type.";
        internal const string PropertyTypeMustMatchGetter =
            "Property type must match the value type of getter";
        internal const string PropertyTypeMustMatchSetter =
            "Property type must match the value type of setter";
        internal const string ReferenceEqualityNotDefined =
            "Reference equality is not defined for the types '{0}' and '{1}'.";
        internal const string SameKeyExistsInExpando =
            "An element with the same key '{0}' already exists in the ExpandoObject.";
        internal const string SetterHasNoParams =
            "Setter must have parameters.";
        internal const string SetterMustBeVoid =
            "Setter should have void type.";
        internal const string TypeContainsGenericParameters =
            "Type {0} contains generic parameters";
        internal const string TypeIsGeneric =
            "Type {0} is a generic type definition";
        internal const string TypeMissingDefaultConstructor =
            "Type '{0}' does not have a default constructor";
        internal const string TypeMustBeDerivedFromSystemDelegate =
            "Type must be derived from System.Delegate";
        internal const string TypeMustNotBeByRef =
            "Type must not be ByRef";
        internal const string TypeMustNotBePointer =
            "Type must not be a pointer type";
        internal const string TypeNotIEnumerable =
            "Type '{0}' is not IEnumerable";
        internal const string UnaryOperatorNotDefined =
            "The unary operator {0} is not defined for the type '{1}'.";
        internal const string UnhandledBinary =
            "Unhandled binary: {0}";
        internal const string UnhandledBindingType =
            "Unhandled Binding Type: {0}";
        internal const string UnhandledUnary =
            "Unhandled unary: {0}";
        internal const string UnknownBindingType =
            "Unknown binding type";
        internal const string UserDefinedOpMustHaveConsistentTypes =
            "The user-defined operator method '{1}' for operator '{0}' must have identical parameter and return types.";
        internal const string UserDefinedOperatorMustBeStatic =
            "User-defined operator method '{0}' must be static.";
        internal const string UserDefinedOperatorMustNotBeVoid =
            "User-defined operator method '{0}' must not be void.";

        // NetWasm strings for its expression-construction boundaries.
        internal const string PlatformNotSupported_AddMethodDiscovery =
            "NetWasm cannot discover a collection's Add method; pass the Add MethodInfo or ElementInit initializers instead.";
        internal const string PlatformNotSupported_PropertyLookup =
            "NetWasm cannot look up a property by name; pass the PropertyInfo instead.";
        internal const string PlatformNotSupported_BooleanOperatorLookup =
            "NetWasm cannot look up a type's true and false operators, so user-defined conditional logical operators are unavailable.";
        // Used by System.Linq's Queryable types; CoreLib keeps no internal types in System.Linq,
        // which the System.Linq and System.Linq.AsyncEnumerable friend assemblies declare their own in.
        internal const string ArgumentNotValid =
            "Argument {0} is not valid";
        internal const string PlatformNotSupported_ConstructorLookup =
            "NetWasm cannot look up a reference type's parameterless constructor; pass the ConstructorInfo instead.";
        internal const string PlatformNotSupported_OperatorLookup =
            "NetWasm cannot look up a user-defined operator by name; pass the operator MethodInfo instead.";
        internal const string PlatformNotSupported_RuntimeLambdaType =
            "NetWasm cannot create an Expression<TDelegate> for a delegate type supplied at run time; use Expression.Lambda<TDelegate> instead.";
        internal const string PlatformNotSupported_CustomDelegateType =
            "NetWasm cannot create a delegate type at run time; a delegate with by-ref, pointer or by-ref-like types, or more than 16 parameters, needs one.";

        // NetWasm: arguments are rendered by kind rather than through string.Format, so formatting
        // an error message does not make every constructed type's ToString override, or type and
        // member names, reachable from programs that use expressions. Strings and integers render
        // as text; types, members and other values, which would need that metadata, render as "?".
        internal static string Format(string format, object? arg0) =>
            Format(format, new[] { arg0 });

        internal static string Format(string format, object? arg0, object? arg1) =>
            Format(format, new[] { arg0, arg1 });

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            Format(format, new[] { arg0, arg1, arg2 });

        private static string Format(string format, object?[] args)
        {
            var builder = new System.Text.StringBuilder(format.Length);
            for (int i = 0; i < format.Length; i++)
            {
                int index = i + 2 < format.Length && format[i] == '{' && format[i + 2] == '}' ? format[i + 1] - '0' : -1;
                if (index >= 0 && index < args.Length)
                {
                    builder.Append(Text(args[index]));
                    i += 2;
                }
                else
                {
                    builder.Append(format[i]);
                }
            }
            return builder.ToString();
        }

        private static string Text(object? arg) => arg switch
        {
            null => string.Empty,
            string text => text,
            int number => number.ToString(),
            _ => "?",
        };
        internal const string ConversionIsNotSupportedForArithmeticTypes =
            "Conversion is not supported for arithmetic types without operator overloading.";
        internal const string ExpressionMustBeWriteable =
            "Expression must be writeable";
        internal const string OverloadOperatorTypeDoesNotMatchConversionType =
            "The return type of overload method for operator '{0}' does not match the parameter type of conversion method '{1}'.";
        internal const string UserDefinedOpMustHaveValidReturnType =
            "The user-defined operator method '{1}' for operator '{0}' must return the same type as its parameter or a derived type.";
        internal const string ExpressionTypeDoesNotMatchAssignment =
            "Expression of type '{0}' cannot be used for assignment to type '{1}'";
        internal const string ArgumentMustNotHaveValueType =
            "Argument must not have a value type.";
        internal const string AllCaseBodiesMustHaveSameType =
            "All case bodies and the default body must have the same type.";
        internal const string AllTestValuesMustHaveSameType =
            "All test values must have the same type.";
        internal const string BodyOfCatchMustHaveSameTypeAsBodyOfTry =
            "Body of catch must have the same type as body of try.";
        internal const string DefaultBodyMustBeSupplied =
            "Default body must be supplied if case bodies are not System.Void.";
        internal const string EqualityMustReturnBoolean =
            "The user-defined equality method '{0}' must return a boolean value.";
        internal const string ExpressionTypeDoesNotMatchLabel =
            "Expression of type '{0}' cannot be used for label of type '{1}'";
        internal const string FaultCannotHaveCatchOrFinally =
            "fault cannot be used with catch or finally clauses";
        internal const string LabelMustBeVoidOrHaveExpression =
            "Label type must be System.Void if an expression is not supplied";
        internal const string LabelTypeMustBeVoid =
            "Type must be System.Void for this label argument";
        internal const string StartEndMustBeOrdered =
            "Start and End must be well ordered";
        internal const string SwitchValueTypeDoesNotMatchComparisonMethodParameter =
            "Switch value of type '{0}' cannot be used for the comparison method parameter of type '{1}'";
        internal const string TestValueTypeDoesNotMatchComparisonMethodParameter =
            "Test value of type '{0}' cannot be used for the comparison method parameter of type '{1}'";
        internal const string TryMustHaveCatchFinallyOrFault =
            "try must have at least one catch, finally, or fault clause";
        internal const string VariableMustNotBeByRef =
            "Variable '{0}' uses unsupported type '{1}'. Reference types are not supported for variables.";
        internal const string IncorrectNumberOfTypeArgsForFunc =
            "An incorrect number of type arguments were specified for the declaration of a Func type.";
        internal const string IncorrectNumberOfTypeArgsForAction =
            "An incorrect number of type arguments were specified for the declaration of an Action type.";
        internal const string MustRewriteWithoutMethod =
            "Rewritten expression calls operator method '{0}', but the original node had no operator method. If this is intentional, override '{1}' and change it to allow this rewrite.";
    }
}
