// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: XML schema reading (XMLSchema.SetProperties) and writing (XmlTreeGen.AddXdoProperties) discover the
// properties of DataSet, DataTable, DataColumn and DataRelation through TypeDescriptor, which needs reflection that
// NetWasm does not have. These descriptors describe the same public properties in declaration order (the order
// reflection reports them), with upstream's [DefaultValue], ShouldSerialize methods, [Browsable] and
// [DesignerSerializationVisibility], and ReflectPropertyDescriptor's ShouldSerializeValue and read-only SetValue rules.

using System.ComponentModel;
using System.Globalization;

namespace System.Data
{
    internal sealed class DataSchemaPropertyDescriptor : PropertyDescriptor
    {
        private readonly Type _componentType;
        private readonly Type _propertyType;
        private readonly Func<object, object?> _getter;
        private readonly Action<object, object?>? _setter;
        private readonly bool _hasDefault;
        private readonly object? _defaultValue;
        private readonly Func<object, bool>? _shouldSerialize;

        internal DataSchemaPropertyDescriptor(Type componentType, string name, Type propertyType, Func<object, object?> getter,
            Action<object, object?>? setter, bool hasDefault, object? defaultValue, Func<object, bool>? shouldSerialize, Attribute[] attributes)
            : base(name, attributes)
        {
            _componentType = componentType;
            _propertyType = propertyType;
            _getter = getter;
            _setter = setter;
            _hasDefault = hasDefault;
            _defaultValue = defaultValue;
            _shouldSerialize = shouldSerialize;
        }

        public override Type ComponentType => _componentType;

        public override Type PropertyType => _propertyType;

        public override bool IsReadOnly => _setter is null;

        public override object? GetValue(object? component) => _getter(component!);

        public override void SetValue(object? component, object? value)
        {
            // ReflectPropertyDescriptor ignores SetValue on a read-only property.
            _setter?.Invoke(component!, value);
            OnValueChanged(component, EventArgs.Empty);
        }

        public override bool CanResetValue(object component) =>
            !IsReadOnly && (_hasDefault ? !Equals(_defaultValue, GetValue(component)) : _shouldSerialize is not null && _shouldSerialize(component));

        public override void ResetValue(object component)
        {
            if (_hasDefault)
            {
                SetValue(component, _defaultValue);
            }
        }

        public override bool ShouldSerializeValue(object component)
        {
            if (IsReadOnly)
            {
                return _shouldSerialize is not null ? _shouldSerialize(component) : Attributes.Contains(DesignerSerializationVisibilityAttribute.Content);
            }
            if (!_hasDefault)
            {
                return _shouldSerialize is null || _shouldSerialize(component);
            }
            return !Equals(_defaultValue, GetValue(component));
        }
    }

    internal static class DataSchemaProperties
    {
        private static PropertyDescriptorCollection? s_dataSet, s_dataTable, s_dataColumn, s_dataRelation;

        /// <summary>The properties TypeDescriptor.GetProperties reports for the instance's schema class.</summary>
        internal static PropertyDescriptorCollection GetProperties(object instance) => instance switch
        {
            DataSet => s_dataSet ??= new PropertyDescriptorCollection(DataSetProperties(), readOnly: true),
            DataTable => s_dataTable ??= new PropertyDescriptorCollection(DataTableProperties(), readOnly: true),
            DataColumn => s_dataColumn ??= new PropertyDescriptorCollection(DataColumnProperties(), readOnly: true),
            DataRelation => s_dataRelation ??= new PropertyDescriptorCollection(DataRelationProperties(), readOnly: true),
            _ => throw new PlatformNotSupportedException(SR.Format(SR.NetWasm_DataSchemaPropertiesUnsupported, instance.GetType().FullName)),
        };

        private static PropertyDescriptor[] DataSetProperties() =>
        [
            new DataSchemaPropertyDescriptor(typeof(DataSet), "RemotingFormat", typeof(SerializationFormat), static c => ((DataSet)c).RemotingFormat, static (c, v) => ((DataSet)c).RemotingFormat = (SerializationFormat)v!, hasDefault: true, defaultValue: SerializationFormat.Xml, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "SchemaSerializationMode", typeof(SchemaSerializationMode), static c => ((DataSet)c).SchemaSerializationMode, static (c, v) => ((DataSet)c).SchemaSerializationMode = (SchemaSerializationMode)v!, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false), DesignerSerializationVisibilityAttribute.Hidden]),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "CaseSensitive", typeof(bool), static c => ((DataSet)c).CaseSensitive, static (c, v) => ((DataSet)c).CaseSensitive = (bool)v!, hasDefault: true, defaultValue: false, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "DefaultViewManager", typeof(DataViewManager), static c => ((DataSet)c).DefaultViewManager, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "EnforceConstraints", typeof(bool), static c => ((DataSet)c).EnforceConstraints, static (c, v) => ((DataSet)c).EnforceConstraints = (bool)v!, hasDefault: true, defaultValue: true, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "DataSetName", typeof(string), static c => ((DataSet)c).DataSetName, static (c, v) => ((DataSet)c).DataSetName = (string)v!, hasDefault: true, defaultValue: "", shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "Namespace", typeof(string), static c => ((DataSet)c).Namespace, static (c, v) => ((DataSet)c).Namespace = (string)v!, hasDefault: true, defaultValue: "", shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "Prefix", typeof(string), static c => ((DataSet)c).Prefix, static (c, v) => ((DataSet)c).Prefix = (string)v!, hasDefault: true, defaultValue: "", shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "ExtendedProperties", typeof(PropertyCollection), static c => ((DataSet)c).ExtendedProperties, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "HasErrors", typeof(bool), static c => ((DataSet)c).HasErrors, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "IsInitialized", typeof(bool), static c => ((DataSet)c).IsInitialized, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "Locale", typeof(CultureInfo), static c => ((DataSet)c).Locale, static (c, v) => ((DataSet)c).Locale = (CultureInfo)v!, hasDefault: false, defaultValue: null, shouldSerialize: static c => ((DataSet)c).ShouldSerializeLocale(), attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "Site", typeof(ISite), static c => ((DataSet)c).Site, static (c, v) => ((DataSet)c).Site = (ISite)v!, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false), DesignerSerializationVisibilityAttribute.Hidden]),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "Relations", typeof(DataRelationCollection), static c => ((DataSet)c).Relations, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [DesignerSerializationVisibilityAttribute.Content]),
            new DataSchemaPropertyDescriptor(typeof(DataSet), "Tables", typeof(DataTableCollection), static c => ((DataSet)c).Tables, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [DesignerSerializationVisibilityAttribute.Content]),
        ];

        private static PropertyDescriptor[] DataTableProperties() =>
        [
            new DataSchemaPropertyDescriptor(typeof(DataTable), "CaseSensitive", typeof(bool), static c => ((DataTable)c).CaseSensitive, static (c, v) => ((DataTable)c).CaseSensitive = (bool)v!, hasDefault: false, defaultValue: null, shouldSerialize: static c => ((DataTable)c).ShouldSerializeCaseSensitive(), attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "IsInitialized", typeof(bool), static c => ((DataTable)c).IsInitialized, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "RemotingFormat", typeof(SerializationFormat), static c => ((DataTable)c).RemotingFormat, static (c, v) => ((DataTable)c).RemotingFormat = (SerializationFormat)v!, hasDefault: true, defaultValue: SerializationFormat.Xml, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "ChildRelations", typeof(DataRelationCollection), static c => ((DataTable)c).ChildRelations, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false), DesignerSerializationVisibilityAttribute.Hidden]),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "Columns", typeof(DataColumnCollection), static c => ((DataTable)c).Columns, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [DesignerSerializationVisibilityAttribute.Content]),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "Constraints", typeof(ConstraintCollection), static c => ((DataTable)c).Constraints, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [DesignerSerializationVisibilityAttribute.Content]),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "DataSet", typeof(DataSet), static c => ((DataTable)c).DataSet, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [DesignerSerializationVisibilityAttribute.Hidden, new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "DefaultView", typeof(DataView), static c => ((DataTable)c).DefaultView, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "DisplayExpression", typeof(string), static c => ((DataTable)c).DisplayExpression, static (c, v) => ((DataTable)c).DisplayExpression = (string)v!, hasDefault: true, defaultValue: "", shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "ExtendedProperties", typeof(PropertyCollection), static c => ((DataTable)c).ExtendedProperties, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "HasErrors", typeof(bool), static c => ((DataTable)c).HasErrors, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "Locale", typeof(CultureInfo), static c => ((DataTable)c).Locale, static (c, v) => ((DataTable)c).Locale = (CultureInfo)v!, hasDefault: false, defaultValue: null, shouldSerialize: static c => ((DataTable)c).ShouldSerializeLocale(), attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "MinimumCapacity", typeof(int), static c => ((DataTable)c).MinimumCapacity, static (c, v) => ((DataTable)c).MinimumCapacity = (int)v!, hasDefault: true, defaultValue: 50, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "ParentRelations", typeof(DataRelationCollection), static c => ((DataTable)c).ParentRelations, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false), DesignerSerializationVisibilityAttribute.Hidden]),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "PrimaryKey", typeof(DataColumn[]), static c => ((DataTable)c).PrimaryKey, static (c, v) => ((DataTable)c).PrimaryKey = (DataColumn[])v!, hasDefault: false, defaultValue: null, shouldSerialize: static c => ((DataTable)c).ShouldSerializePrimaryKey(), attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "Rows", typeof(DataRowCollection), static c => ((DataTable)c).Rows, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "TableName", typeof(string), static c => ((DataTable)c).TableName, static (c, v) => ((DataTable)c).TableName = (string)v!, hasDefault: true, defaultValue: "", shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "Namespace", typeof(string), static c => ((DataTable)c).Namespace, static (c, v) => ((DataTable)c).Namespace = (string)v!, hasDefault: false, defaultValue: null, shouldSerialize: static c => ((DataTable)c).ShouldSerializeNamespace(), attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "Prefix", typeof(string), static c => ((DataTable)c).Prefix, static (c, v) => ((DataTable)c).Prefix = (string)v!, hasDefault: true, defaultValue: "", shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataTable), "Site", typeof(ISite), static c => ((DataTable)c).Site, static (c, v) => ((DataTable)c).Site = (ISite)v!, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false), DesignerSerializationVisibilityAttribute.Hidden]),
        ];

        private static PropertyDescriptor[] DataColumnProperties() =>
        [
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "AllowDBNull", typeof(bool), static c => ((DataColumn)c).AllowDBNull, static (c, v) => ((DataColumn)c).AllowDBNull = (bool)v!, hasDefault: true, defaultValue: true, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "AutoIncrement", typeof(bool), static c => ((DataColumn)c).AutoIncrement, static (c, v) => ((DataColumn)c).AutoIncrement = (bool)v!, hasDefault: true, defaultValue: false, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "AutoIncrementSeed", typeof(long), static c => ((DataColumn)c).AutoIncrementSeed, static (c, v) => ((DataColumn)c).AutoIncrementSeed = (long)v!, hasDefault: true, defaultValue: (long)0, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "AutoIncrementStep", typeof(long), static c => ((DataColumn)c).AutoIncrementStep, static (c, v) => ((DataColumn)c).AutoIncrementStep = (long)v!, hasDefault: true, defaultValue: (long)1, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "Caption", typeof(string), static c => ((DataColumn)c).Caption, static (c, v) => ((DataColumn)c).Caption = (string)v!, hasDefault: false, defaultValue: null, shouldSerialize: static c => ((DataColumn)c).ShouldSerializeCaption(), attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "ColumnName", typeof(string), static c => ((DataColumn)c).ColumnName, static (c, v) => ((DataColumn)c).ColumnName = (string)v!, hasDefault: true, defaultValue: "", shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "Prefix", typeof(string), static c => ((DataColumn)c).Prefix, static (c, v) => ((DataColumn)c).Prefix = (string)v!, hasDefault: true, defaultValue: "", shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "DataType", typeof(Type), static c => ((DataColumn)c).DataType, static (c, v) => ((DataColumn)c).DataType = (Type)v!, hasDefault: true, defaultValue: typeof(string), shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "DateTimeMode", typeof(DataSetDateTime), static c => ((DataColumn)c).DateTimeMode, static (c, v) => ((DataColumn)c).DateTimeMode = (DataSetDateTime)v!, hasDefault: true, defaultValue: DataSetDateTime.UnspecifiedLocal, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "DefaultValue", typeof(object), static c => ((DataColumn)c).DefaultValue, static (c, v) => ((DataColumn)c).DefaultValue = (object)v!, hasDefault: false, defaultValue: null, shouldSerialize: static c => ((DataColumn)c).ShouldSerializeDefaultValue(), attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "Expression", typeof(string), static c => ((DataColumn)c).Expression, static (c, v) => ((DataColumn)c).Expression = (string)v!, hasDefault: true, defaultValue: "", shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "ExtendedProperties", typeof(PropertyCollection), static c => ((DataColumn)c).ExtendedProperties, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "MaxLength", typeof(int), static c => ((DataColumn)c).MaxLength, static (c, v) => ((DataColumn)c).MaxLength = (int)v!, hasDefault: true, defaultValue: -1, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "Namespace", typeof(string), static c => ((DataColumn)c).Namespace, static (c, v) => ((DataColumn)c).Namespace = (string)v!, hasDefault: false, defaultValue: null, shouldSerialize: static c => ((DataColumn)c).ShouldSerializeNamespace(), attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "Ordinal", typeof(int), static c => ((DataColumn)c).Ordinal, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [DesignerSerializationVisibilityAttribute.Hidden, new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "ReadOnly", typeof(bool), static c => ((DataColumn)c).ReadOnly, static (c, v) => ((DataColumn)c).ReadOnly = (bool)v!, hasDefault: true, defaultValue: false, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "Table", typeof(DataTable), static c => ((DataColumn)c).Table, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [DesignerSerializationVisibilityAttribute.Hidden, new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "Unique", typeof(bool), static c => ((DataColumn)c).Unique, static (c, v) => ((DataColumn)c).Unique = (bool)v!, hasDefault: true, defaultValue: false, shouldSerialize: null, attributes: [DesignerSerializationVisibilityAttribute.Hidden]),
            new DataSchemaPropertyDescriptor(typeof(DataColumn), "ColumnMapping", typeof(MappingType), static c => ((DataColumn)c).ColumnMapping, static (c, v) => ((DataColumn)c).ColumnMapping = (MappingType)v!, hasDefault: true, defaultValue: MappingType.Element, shouldSerialize: null, attributes: []),
        ];

        private static PropertyDescriptor[] DataRelationProperties() =>
        [
            new DataSchemaPropertyDescriptor(typeof(DataRelation), "ChildColumns", typeof(DataColumn[]), static c => ((DataRelation)c).ChildColumns, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataRelation), "ChildTable", typeof(DataTable), static c => ((DataRelation)c).ChildTable, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataRelation), "DataSet", typeof(DataSet), static c => ((DataRelation)c).DataSet, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [DesignerSerializationVisibilityAttribute.Hidden, new BrowsableAttribute(false)]),
            new DataSchemaPropertyDescriptor(typeof(DataRelation), "ParentColumns", typeof(DataColumn[]), static c => ((DataRelation)c).ParentColumns, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataRelation), "ParentTable", typeof(DataTable), static c => ((DataRelation)c).ParentTable, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataRelation), "RelationName", typeof(string), static c => ((DataRelation)c).RelationName, static (c, v) => ((DataRelation)c).RelationName = (string)v!, hasDefault: true, defaultValue: "", shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataRelation), "Nested", typeof(bool), static c => ((DataRelation)c).Nested, static (c, v) => ((DataRelation)c).Nested = (bool)v!, hasDefault: true, defaultValue: false, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataRelation), "ParentKeyConstraint", typeof(UniqueConstraint), static c => ((DataRelation)c).ParentKeyConstraint, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataRelation), "ChildKeyConstraint", typeof(ForeignKeyConstraint), static c => ((DataRelation)c).ChildKeyConstraint, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: []),
            new DataSchemaPropertyDescriptor(typeof(DataRelation), "ExtendedProperties", typeof(PropertyCollection), static c => ((DataRelation)c).ExtendedProperties, null, hasDefault: false, defaultValue: null, shouldSerialize: null, attributes: [new BrowsableAttribute(false)]),
        ];
    }
}
