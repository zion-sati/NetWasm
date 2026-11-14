// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Concurrent;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Serialization;

namespace System.Data.Common
{
    internal sealed class SqlUdtStorage : DataStorage
    {
        private object[] _values = default!; // Late-initialized
        private readonly bool _implementsIXmlSerializable;
        private readonly bool _implementsIComparable;

        private static readonly ConcurrentDictionary<Type, object> s_typeToNull = new ConcurrentDictionary<Type, object>();

        public SqlUdtStorage(DataColumn column, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] Type type)
        : this(column, type, GetStaticNullForUdtType(type))
        {
        }

        private SqlUdtStorage(DataColumn column, Type type, object nullValue)
        : base(column, type, nullValue, nullValue, typeof(ICloneable).IsAssignableFrom(type), GetStorageType(type))
        {
            _implementsIXmlSerializable = typeof(IXmlSerializable).IsAssignableFrom(type);
            _implementsIComparable = typeof(IComparable).IsAssignableFrom(type);
        }

        // to support oracle types and other INUllable types that have static Null as field
        internal static object GetStaticNullForUdtType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] Type type) => s_typeToNull.GetOrAdd(type, GetStaticNullForUdtTypeCore);

        // NetWasm: upstream finds a user-defined INullable type's static Null property or field by reflection, which
        // NetWasm does not have (classified unsupported); the built-in SqlTypes have their own storage classes.
        private static object GetStaticNullForUdtTypeCore(Type type)
        {
            throw new PlatformNotSupportedException(SR.Format(SR.NetWasm_DataUdtNullUnsupported, type.FullName));
        }

        public override bool IsNull(int record)
        {
            return (((INullable)_values[record]).IsNull);
        }

        public override object Aggregate(int[] records, AggregateType kind)
        {
            throw ExceptionBuilder.AggregateException(kind, _dataType);
        }

        public override int Compare(int recordNo1, int recordNo2)
        {
            return (CompareValueTo(recordNo1, _values[recordNo2]));
        }

        public override int CompareValueTo(int recordNo1, object? value)
        {
            if (DBNull.Value == value)
            {
                // it is not meaningful compare UDT with DBNull.Value
                value = _nullValue;
            }
            if (_implementsIComparable)
            {
                IComparable comparable = (IComparable)_values[recordNo1];
                return comparable.CompareTo(value);
            }
            else if (_nullValue == value)
            {
                INullable nullableValue = (INullable)_values[recordNo1];
                return nullableValue.IsNull ? 0 : 1; // left may be null, right is null
            }

            throw ExceptionBuilder.IComparableNotImplemented(_dataType.FullName!); // NetWasm: no assembly metadata
        }

        public override void Copy(int recordNo1, int recordNo2)
        {
            CopyBits(recordNo1, recordNo2);
            _values[recordNo2] = _values[recordNo1];
        }

        public override object Get(int recordNo)
        {
            return (_values[recordNo]);
        }

        public override void Set(int recordNo, object value)
        {
            if (DBNull.Value == value)
            {
                _values[recordNo] = _nullValue;
                SetNullBit(recordNo, true);
            }
            else if (null == value)
            {
                if (_isValueType)
                {
                    throw ExceptionBuilder.StorageSetFailed();
                }
                else
                {
                    _values[recordNo] = _nullValue;
                    SetNullBit(recordNo, true);
                }
            }
            else if (!_dataType.IsInstanceOfType(value))
            {
                throw ExceptionBuilder.StorageSetFailed();
            }
            else
            {
                // do not clone the value
                _values[recordNo] = value;
                SetNullBit(recordNo, false);
            }
        }

        public override void SetCapacity(int capacity)
        {
            Array.Resize(ref _values, capacity);
            base.SetCapacity(capacity);
        }

        // Prevent inlining so that reflection calls are not moved to caller that may be in a different assembly that may have a different grant set.
        [MethodImpl(MethodImplOptions.NoInlining)]
        [RequiresUnreferencedCode(DataSet.RequiresUnreferencedCodeMessage)]
        [RequiresDynamicCode(DataSet.RequiresDynamicCodeMessage)]
        public override object ConvertXmlToObject(string s)
        {
            if (_implementsIXmlSerializable)
            {
                // NetWasm: activating the user-defined type needs reflection (classified unsupported).
                throw DataTable.ReflectiveActivationUnsupported(_dataType);
            }

            StringReader strreader = new StringReader(s);
            XmlSerializer deserializerWithOutRootAttribute = ObjectStorage.GetXmlSerializer(_dataType);
            return (deserializerWithOutRootAttribute.Deserialize(strreader))!;
        }

        // Prevent inlining so that reflection calls are not moved to caller that may be in a different assembly that may have a different grant set.
        [MethodImpl(MethodImplOptions.NoInlining)]
        [RequiresUnreferencedCode(DataSet.RequiresUnreferencedCodeMessage)]
        [RequiresDynamicCode(DataSet.RequiresDynamicCodeMessage)]
        public override object ConvertXmlToObject(XmlReader xmlReader, XmlRootAttribute? xmlAttrib)
        {
            if (null == xmlAttrib)
            {
                // NetWasm: upstream resolves msdata:InstanceType with Type.GetType and activates it by reflection
                // (classified unsupported).
                throw DataTable.ReflectiveActivationUnsupported(_dataType);
            }
            else
            {
                XmlSerializer deserializerWithRootAttribute = ObjectStorage.GetXmlSerializer(_dataType, xmlAttrib);
                return (deserializerWithRootAttribute.Deserialize(xmlReader))!;
            }
        }

        [RequiresUnreferencedCode(DataSet.RequiresUnreferencedCodeMessage)]
        [RequiresDynamicCode(DataSet.RequiresDynamicCodeMessage)]
        public override string ConvertObjectToXml(object value)
        {
            StringWriter strwriter = new StringWriter(FormatProvider);
            if (_implementsIXmlSerializable)
            {
                using (XmlTextWriter xmlTextWriter = new XmlTextWriter(strwriter))
                {
                    ((IXmlSerializable)value).WriteXml(xmlTextWriter);
                }
            }
            else
            {
                XmlSerializer serializerWithOutRootAttribute = ObjectStorage.GetXmlSerializer(value.GetType());
                serializerWithOutRootAttribute.Serialize(strwriter, value);
            }
            return (strwriter.ToString());
        }

        [RequiresUnreferencedCode(DataSet.RequiresUnreferencedCodeMessage)]
        [RequiresDynamicCode(DataSet.RequiresDynamicCodeMessage)]
        public override void ConvertObjectToXml(object value, XmlWriter xmlWriter, XmlRootAttribute? xmlAttrib)
        {
            if (null == xmlAttrib)
            {
                Debug.Assert(xmlWriter is DataTextWriter, "Invalid DataTextWriter is being passed to customer");
                ((IXmlSerializable)value).WriteXml(xmlWriter);
            }
            else
            {
                // we support polymorphism only for types that implements IXmlSerializable.
                // Assumption: value is the same type as DataType

                XmlSerializer serializerWithRootAttribute = ObjectStorage.GetXmlSerializer(_dataType, xmlAttrib);
                serializerWithRootAttribute.Serialize(xmlWriter, value);
            }
        }

        protected override object GetEmptyStorage(int recordCount)
        {
            return new object[recordCount];
        }

        protected override void CopyValue(int record, object store, BitArray nullbits, int storeIndex)
        {
            object[] typedStore = (object[])store;
            typedStore[storeIndex] = _values[record];
            nullbits.Set(storeIndex, IsNull(record));
        }

        protected override void SetStorage(object store, BitArray nullbits)
        {
            _values = (object[])store;
            //SetNullStorage(nullbits);
        }
    }
}
