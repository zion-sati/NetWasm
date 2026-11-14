// From the compiler harness's ExtenderDescriptorContractsCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
#pragma warning disable CS0618
namespace ExtenderFixture;
sealed class Property : PropertyDescriptor
{
    public Property():base("Value",null){}
    public override Type ComponentType=>typeof(object);
    public override Type PropertyType=>typeof(int);
    public override bool IsReadOnly=>false;
    public override object GetValue(object component)=>42;
    public override void SetValue(object component,object value){}
    public override bool CanResetValue(object component)=>false;
    public override void ResetValue(object component){}
    public override bool ShouldSerializeValue(object component)=>true;
}
sealed class Event : EventDescriptor
{
    public Event():base("Changed",null){}
    public override Type ComponentType=>typeof(object);
    public override Type EventType=>typeof(Action);
    public override bool IsMulticast=>true;
    public override void AddEventHandler(object component,Delegate value){}
    public override void RemoveEventHandler(object component,Delegate value){}
}
sealed class Handler : IComNativeDescriptorHandler
{
    public readonly object Item=new object();
    public readonly object Editor=new object();
    public readonly AttributeCollection Attributes=new AttributeCollection(new BrowsableAttribute(true));
    public readonly TypeConverter Converter=new TypeConverter();
    public readonly Property Property=new Property();
    public readonly Event Event=new Event();
    public readonly Attribute[] Filter=new Attribute[0];
    public readonly EventDescriptorCollection Events;
    public readonly PropertyDescriptorCollection Properties;
    public Handler(){Events=new EventDescriptorCollection(new EventDescriptor[]{Event});Properties=new PropertyDescriptorCollection(new PropertyDescriptor[]{Property});}
    public AttributeCollection GetAttributes(object component)=>component==Item?Attributes:null;
    public string GetClassName(object component)=>component==Item?"class":null;
    public TypeConverter GetConverter(object component)=>component==Item?Converter:null;
    public EventDescriptor GetDefaultEvent(object component)=>component==Item?Event:null;
    public PropertyDescriptor GetDefaultProperty(object component)=>component==Item?Property:null;
    public object GetEditor(object component,Type editorType)=>component==Item && editorType==typeof(object)?Editor:null;
    public string GetName(object component)=>component==Item?"item":null;
    public EventDescriptorCollection GetEvents(object component)=>component==Item?Events:null;
    public EventDescriptorCollection GetEvents(object component,Attribute[] attrs)=>component==Item && ReferenceEquals(attrs,Filter)?Events:null;
    public PropertyDescriptorCollection GetProperties(object component,Attribute[] attrs)=>component==Item && ReferenceEquals(attrs,Filter)?Properties:null;
    public object GetPropertyValue(object component,string name,ref bool success){success=component==Item && name=="Value";return success?42:null;}
    public object GetPropertyValue(object component,int id,ref bool success){success=component==Item && id==7;return success?43:null;}
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        var attribute=new ExtenderProvidedPropertyAttribute();var equal=new ExtenderProvidedPropertyAttribute();
        if(attribute.ExtenderProperty!=null || attribute.Provider!=null || attribute.ReceiverType!=null || !attribute.IsDefaultAttribute() || !attribute.Equals(attribute) || !attribute.Equals(equal) || attribute.Equals(null) || attribute.Equals(new object()) || attribute.GetHashCode()!=equal.GetHashCode())return 1;
        var handler=new Handler();IComNativeDescriptorHandler api=handler;var item=handler.Item;
        if(api.GetAttributes(item)!=handler.Attributes || api.GetClassName(item)!="class" || api.GetConverter(item)!=handler.Converter || api.GetDefaultEvent(item)!=handler.Event || api.GetDefaultProperty(item)!=handler.Property)return 2;
        if(api.GetEditor(item,typeof(object))!=handler.Editor || api.GetName(item)!="item" || api.GetEvents(item)!=handler.Events || api.GetEvents(item,handler.Filter)!=handler.Events || api.GetProperties(item,handler.Filter)!=handler.Properties)return 3;
        var success=false;if((int)api.GetPropertyValue(item,"Value",ref success)!=42 || !success)return 4;
        success=false;if((int)api.GetPropertyValue(item,7,ref success)!=43 || !success)return 5;
        if(api.GetPropertyValue(item,"missing",ref success)!=null || success)return 6;
        success=true;if(api.GetPropertyValue(item,8,ref success)!=null || success)return 7;
        return 42;
    }
}
