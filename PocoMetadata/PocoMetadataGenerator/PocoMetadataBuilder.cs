﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Breeze.PocoMetadata
{
    /// <summary>
    /// Builds a data structure containing the metadata required by Breeze.
    /// <see cref="http://www.breezejs.com/documentation/breeze-metadata-format"/>
    /// </summary>
    public class PocoMetadataBuilder
    {
        private Metadata _map;
        private List<Dictionary<string, object>> _typeList;
        private Dictionary<string, object> _resourceMap;
        private HashSet<string> _typeNames;
        private List<Dictionary<string, object>> _enumList;
        private List<Type> _types;
        private IEntityDescription _describer;

        public PocoMetadataBuilder(IEntityDescription describer)
        {
            this._describer = describer;
        }

        /// <summary>
        /// Build the Breeze metadata as a nested Dictionary.  
        /// The result can be converted to JSON and sent to the Breeze client.
        /// </summary>
        /// <param name="classMeta">Entity metadata types to include in the metadata</param>
        /// <returns></returns>
        public Metadata BuildMetadata(IEnumerable<Type> types)
        {
            InitMap();
            _types = types.ToList();

            foreach (var t in _types)
            {
                AddType(t);
            }
            return _map;
        }

        /// <summary>
        /// Populate the metadata header.
        /// </summary>
        void InitMap()
        {
            _map = new Metadata();
            _typeList = new List<Dictionary<string, object>>();
            _typeNames = new HashSet<string>();
            _resourceMap = new Dictionary<string, object>();
            _map.ForeignKeyMap = new Dictionary<string, string>();
            _enumList = new List<Dictionary<string, object>>();
            _map.Add("localQueryComparisonOptions", "caseInsensitiveSQL");
            _map.Add("structuralTypes", _typeList);
            _map.Add("resourceEntityTypeMap", _resourceMap);
            _map.Add("enumTypes", _enumList);
        }

        /// <summary>
        /// Add the metadata for an entity.
        /// </summary>
        /// <param name="type">Type for which metadata is being generated</param>
        void AddType(Type type)
        {
            // "Customer:#Breeze.Models.NorthwindIBModel": {
            var classKey = type.Name + ":#" + type.Namespace;
            var cmap = new Dictionary<string, object>();
            _typeList.Add(cmap);

            cmap.Add("shortName", type.Name);
            cmap.Add("namespace", type.Namespace);

            // Only identify the base type if it is also in the type list
            if (_types.Contains(type.BaseType))
            {
                var baseTypeName = type.BaseType.Name + ":#" + type.BaseType.Namespace;
                cmap.Add("baseTypeName", baseTypeName);
            }

            // Get the autoGeneratedKeyType for this type
            var keyGenerator = _describer.GetAutoGeneratedKeyType(type);
            if (keyGenerator != null)
            {
                cmap.Add("autoGeneratedKeyType", keyGenerator);
            }

            var resourceName = _describer.GetResourceName(type);
            cmap.Add("defaultResourceName", resourceName);
            _resourceMap.Add(resourceName, classKey);

            var dataList = new List<Dictionary<string, object>>();
            cmap.Add("dataProperties", dataList);
            var navList = new List<Dictionary<string, object>>();
            cmap.Add("navigationProperties", navList);

            AddClassProperties(type, dataList, navList);
        }

        /// <summary>
        /// Add the properties for an entity type.
        /// </summary>
        /// <param name="type">Type for which metadata is being generated</param>
        /// <param name="dataList">will be populated with the data properties of the entity</param>
        /// <param name="navList">will be populated with the navigation properties of the entity</param>
        void AddClassProperties(Type type, List<Dictionary<string, object>> dataList, List<Dictionary<string, object>> navList)
        {
            // Get properties for the given class
            var propertyInfos = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Exclude properties that are declared on a base class that is also in the type list
            // those properties will be defined in the metadata for the base class
            propertyInfos = propertyInfos.Where(p => p.DeclaringType.Equals(type) || !_types.Contains(p.DeclaringType)).ToArray();

            foreach(var propertyInfo in propertyInfos)
            {
                var elementType = GetElementType(propertyInfo.PropertyType);
                if (_types.Contains(elementType))
                {
                    // association to another entity in the metadata list; skip until later
                    // TODO complex types need to be handled too
                }
                else
                {
                    // data property
                    var isKey = _describer.IsKeyProperty(type, propertyInfo);
                    var isVersion = _describer.IsVersionProperty(type, propertyInfo);

                    var dmap = MakeDataProperty(propertyInfo, isKey, isVersion);
                    dataList.Add(dmap);
                }

                if (propertyInfo.PropertyType.IsEnum)
                {
                    var types = propertyInfo.PropertyType.GetGenericArguments();
                    if (types.Length > 0)
                    {
                        var realType = types[0];
                        if (!_enumList.Exists(x => x.ContainsValue(realType.Name)))
                        {
                            string[] enumNames = Enum.GetNames(realType);
                            var p = new Dictionary<string, object>();
                            p.Add("shortName", realType.Name);
                            p.Add("namespace", realType.Namespace);
                            p.Add("values", enumNames);
                            _enumList.Add(p);
                        }
                    }
                }
            }

            // Process again to handle the association properties
            foreach (var propertyInfo in propertyInfos)
            {
                var elementType = GetElementType(propertyInfo.PropertyType);
                if (_types.Contains(elementType))
                {
                    // now handle association to other entities
                    // navigation property
                    var assProp = MakeAssociationProperty(propertyInfo, type, dataList, false);
                    navList.Add(assProp);
                }
            }
        }

        /// <summary>
        /// Make data property metadata for the entity property.  
        /// Attributes one the property are used to set some metadata values.
        /// </summary>
        /// <param name="propertyInfo">Property info for the property</param>
        /// <param name="isKey">true if this property is part of the key for the entity</param>
        /// <param name="isVersion">true if this property contains the version of the entity (for a concurrency strategy)</param>
        /// <returns>Dictionary of metadata for the property</returns>
        private Dictionary<string, object> MakeDataProperty(PropertyInfo propertyInfo, bool isKey, bool isVersion)
        {
            var nullableType = Nullable.GetUnderlyingType(propertyInfo.PropertyType);
            var isNullable = nullableType != null || !propertyInfo.PropertyType.IsValueType;
            var propType = nullableType ?? propertyInfo.PropertyType;

            var dmap = new Dictionary<string, object>();
            dmap.Add("nameOnServer", propertyInfo.Name);
            dmap.Add("dataType", propType.Name);
            if (!isNullable) dmap.Add("isNullable", "false");

            AddAttributesToDataProperty(propertyInfo, dmap);

            if (isKey) dmap["isPartOfKey"] = true;
            if (isVersion) dmap["concurrencyMode"] = "Fixed";

            return dmap;
        }

        /// <summary>
        /// Make association property metadata for the entity.
        /// Also populates the ForeignKeyMap which is used for related-entity fixup in NHContext.FixupRelationships
        /// </summary>
        /// <param name="propertyInfo">Property info describing the property</param>
        /// <param name="containingType">Type containing the property</param>
        /// <param name="dataProperties">Data properties already collected for the containingType.  "isPartOfKey" may be added to a property.</param>
        /// <param name="isKey">Whether the property is part of the key</param>
        /// <returns></returns>
        private Dictionary<string, object> MakeAssociationProperty(PropertyInfo propertyInfo, Type containingType, List<Dictionary<string, object>> dataProperties, bool isKey)
        {
            var nmap = new Dictionary<string, object>();
            nmap.Add("nameOnServer", propertyInfo.Name);

            var propType = propertyInfo.PropertyType;
            var isCollection = IsCollectionType(propType);
            var relatedEntityType = isCollection ? GetElementType(propType) : propType;
            nmap.Add("entityTypeName", relatedEntityType.Name + ":#" + relatedEntityType.Namespace);
            nmap.Add("isScalar", !isCollection);

            // the associationName must be the same at both ends of the association.
            // TODO fix this.  May have to add associationName after all entities are added
            string[] columnNames = new string[] { propertyInfo.Name };
            nmap.Add("associationName", GetAssociationName(containingType.Name, relatedEntityType.Name, columnNames));

            AddAttributesToNavProperty(propertyInfo, nmap);

            return nmap;
        }


        /// <summary>
        /// Add to the data property map based on attributes on the class member.  Checks a list of known annotations.
        /// </summary>
        /// <param name="memberInfo">Property or field of the class for which metadata is being generated</param>
        /// <param name="dmap">Data property definition</param>
        private void AddAttributesToDataProperty(MemberInfo memberInfo, Dictionary<string, object> dmap)
        {
            var validators = new List<Dictionary<string, object>>();
            var attributes = memberInfo.GetCustomAttributes();
            foreach (var attr in attributes)
            {
                var name = attr.GetType().Name;
                if (name.EndsWith("Attribute"))
                {
                    // get the name without "Attribute" on the end
                    name = name.Substring(0, name.Length - "Attribute".Length);
                }

                if (name == "Key" || name == "PrimaryKey")
                {
                    dmap["isPartOfKey"] = "true";
                }
                else if (name == "ConcurrencyCheck")
                {
                    dmap["concurrencyMode"] = "Fixed";
                }
                else if (name == "Required")
                {
                    dmap["isNullable"] = "false";
                }
                else if (name == "DefaultValue")
                {
                    dmap["defaultValue"] = GetAttributeValue(attr, "Value");
                }
                else if (name == "MaxLength")
                {
                    dmap["maxLength"] = GetAttributeValue(attr, "Length");
                }
                else if (name == "StringLength")
                {
                    dmap["maxLength"] = GetAttributeValue(attr, "MaximumLength");
                    var min = (int) GetAttributeValue(attr, "MinimumLength");
                    if (min > 0) dmap["minLength"] = min;
                }
                else if (name == "DatabaseGenerated")
                {
                    dmap["isComputed"] = "true";
                }
                else if (name == "ForeignKey")
                {
                    dmap["foreignKey"] = GetAttributeValue(attr, "Name");
                }
                else if (name == "InverseProperty")
                {
                    dmap["inverseProperty"] = GetAttributeValue(attr, "Property");
                }
                else if (name.Contains("Validat"))
                {
                    // Assume some sort of validator.  Add all the properties of the attribute to the validation map
                    validators.Add(new Dictionary<string, object>() {{"name", camelCase(name) }});
                    foreach (var propertyInfo in attr.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.SetProperty | BindingFlags.FlattenHierarchy))
                    {
                        var value = propertyInfo.GetValue(attr);
                        if (value != null)
                        {
                            validators.Add(new Dictionary<string, object>() { { camelCase(propertyInfo.Name), value } });
                        }
                    }
                }
            }

            if (validators.Any())
            {
                dmap.Add("validators", validators);
            }

        }

        /// <summary>
        /// Add to the navigation property map based on attributes on the class member.  Checks a list of known annotations.
        /// </summary>
        /// <param name="memberInfo">Property or field of the class for which metadata is being generated</param>
        /// <param name="nmap">Navigation property definition</param>
        private void AddAttributesToNavProperty(MemberInfo memberInfo, Dictionary<string, object> nmap)
        {
            var attributes = memberInfo.GetCustomAttributes();
            foreach (var attr in attributes)
            {
                var name = attr.GetType().Name;
                if (name.EndsWith("Attribute"))
                {
                    // get the name without "Attribute" on the end
                    name = name.Substring(0, name.Length - "Attribute".Length);
                }

                if (name == "ForeignKey")
                {
                    nmap["foreignKey"] = GetAttributeValue(attr, "Name");
                }
                else if (name == "InverseProperty")
                {
                    nmap["inverseProperty"] = GetAttributeValue(attr, "Property");
                }
            }
        }

        /// <summary>
        /// Get the value of the given property of the attribute
        /// </summary>
        /// <param name="attr">Attribute to inspect</param>
        /// <param name="propertyName">Name of property</param>
        /// <returns></returns>
        private object GetAttributeValue(Attribute attr, string propertyName)
        {
            var propertyInfo = attr.GetType().GetProperty(propertyName);
            var value = propertyInfo.GetValue(attr);
            return value;
        }

        private bool IsCollectionType(Type type)
        {
            return type == typeof(Array)
                || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                || type.GetInterfaces().Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        }

        /// <summary>
        /// Return the element type of a collection type (array or IEnumerable<typeparamref name="T"/>)
        /// For a plain IEnumerable, return System.Object
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private Type GetElementType(Type type)
        {
            if (!IsCollectionType(type)) return type;
            return type.HasElementType ? type.GetElementType() :
                (type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object));
        }

        /// <summary>
        /// Creates an association name from two entity names.
        /// For consistency, puts the entity names in alphabetical order.
        /// </summary>
        /// <param name="name1"></param>
        /// <param name="name2"></param>
        /// <param name="propType">Used to ensure the association name is unique for a type</param>
        /// <returns></returns>
        static string GetAssociationName(string name1, string name2, string[] columnNames)
        {
            var cols = string.Join(" ", columnNames);
            if (name1.CompareTo(name2) < 0)
                return FK + name1 + '_' + name2 + '_' + cols;
            else
                return FK + name2 + '_' + name1 + '_' + cols;
        }
        const string FK = "FK_";

        /// <summary>
        /// Change first letter to lowercase
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private string camelCase(string s)
        {
            if (string.IsNullOrEmpty(s) || !char.IsUpper(s[0]))
            {
                return s;
            }
            string str = char.ToLower(s[0]).ToString();
            if (s.Length > 1)
            {
                str = str + s.Substring(1);
            }
            return str;

        }

    }



    /// <summary>
    /// Metadata describing the entity model.  Converted to JSON to send to Breeze client.
    /// </summary>
    public class Metadata : Dictionary<string, object>
    {
        /// <summary>
        /// Map of relationship name -> foreign key name, e.g. "Customer" -> "CustomerID".
        /// Used for re-establishing the entity relationships from the foreign key values during save.
        /// This part is not sent to the client because it is separate from the base dictionary implementation.
        /// </summary>
        public IDictionary<string, string> ForeignKeyMap;
    }

    public interface IEntityDescription
    {
        /// <summary>
        /// Get the autoGeneratedKeyType value for the given type.  Should be defined even if the actual key property is on a base type.
        /// </summary>
        /// <param name="type">Entity type for which metadata is being generated</param>
        /// <returns>One of:
        /// "Identity" - key is generated by db server, or is a Guid.
        /// "KeyGenerator" - key is generated by code on app server, e.g. using Breeze.ContextProvider.IKeyGenerator 
        /// "None" - key is not auto-generated, but is assigned manually.
        /// null - same as None.
        /// </returns>
        string GetAutoGeneratedKeyType(Type type);

        /// <summary>
        /// Get the server resource name (endpoint) for the given type.  E.g. for entity type Product, it might be "Products".
        /// This value is used by Breeze client when composing a query URL for an entity.
        /// </summary>
        /// <param name="type">Entity type for which metadata is being generated</param>
        /// <returns>Resource name</returns>
        string GetResourceName(Type type);

        /// <summary>
        /// Determine if the property is part of the entity key.
        /// </summary>
        /// <param name="type">Entity type for which metadata is being generated</param>
        /// <param name="propertyInfo">Property being considered</param>
        /// <returns>True if property is part of the entity key, false otherwise</returns>
        bool IsKeyProperty(Type type, PropertyInfo propertyInfo);

        /// <summary>
        /// Determine if the property is a version property used for optimistic concurrency control.
        /// </summary>
        /// <param name="type">Entity type for which metadata is being generated</param>
        /// <param name="propertyInfo">Property being considered</param>
        /// <returns>True if property is the entity's version property, false otherwise</returns>
        bool IsVersionProperty(Type type, PropertyInfo propertyInfo);

    }

    public class EntityDescription : IEntityDescription
    {
        public string GetAutoGeneratedKeyType(Type type)
        {
            return "Identity";
        }

        public string GetResourceName(Type type)
        {
            return Pluralize(type.Name);
        }

        public bool IsKeyProperty(Type type, PropertyInfo propertyInfo)
        {
            return (propertyInfo.Name == "EntityKey");
        }

        public bool IsVersionProperty(Type type, PropertyInfo propertyInfo)
        {
            return false;
        }

        /// <summary>
        /// Lame pluralizer.  Assumes we just need to add a suffix.  
        /// Consider using System.Data.Entity.Design.PluralizationServices.PluralizationService.
        /// </summary>
        /// <param name="s">String to pluralize</param>
        /// <returns>Pseudo-pluralized string</returns>
        public string Pluralize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var last = s.Length - 1;
            var c = s[last];
            switch (c)
            {
                case 'y':
                    return s.Substring(0, last) + "ies";
                default:
                    return s + 's';
            }
        }

    }
}
