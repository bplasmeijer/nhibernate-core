using System;
using System.Collections;
using Iesi.Collections.Generic;
using NHibernate.Engine;
using NHibernate.SqlCommand;
using NHibernate.Util;
using System.Collections.Generic;

namespace NHibernate.Mapping
{
	/// <summary>
	/// Base class for the <see cref="RootClass" /> mapped by <c>&lt;class&gt;</c> and a 
	/// <see cref="Subclass"/> that is mapped by <c>&lt;subclass&gt;</c> or 
	/// <c>&lt;joined-subclass&gt;</c>.
	/// </summary>
	public abstract class PersistentClass : IFilterable, IMetaAttributable
	{
		private static readonly Alias PKAlias = new Alias(15, "PK");

		/// <summary></summary>
		public const string NullDiscriminatorMapping = "null";

		/// <summary></summary>
		public const string NotNullDiscriminatorMapping = "not null";

		private System.Type mappedClass;
		private string discriminatorValue;
		private bool lazy;
		private readonly List<Property> properties = new List<Property>();
		private System.Type proxyInterface;
		private readonly List<Subclass> subclasses = new List<Subclass>();
		private readonly List<Property> subclassProperties = new List<Property>();
		private readonly List<Table> subclassTables = new List<Table>();
		private bool dynamicInsert;
		private bool dynamicUpdate;
		private int batchSize = 1;
		private bool selectBeforeUpdate;
		private OptimisticLockMode optimisticLockMode;
		private IDictionary<string, MetaAttribute> metaAttributes;
		private readonly List<Join> joins = new List<Join>();
		private readonly List<Join> subclassJoins = new List<Join>();
		private readonly IDictionary<string, string> filters = new Dictionary<string, string>();
		private Component identifierMapper;


		private SqlString customSQLInsert;
		private bool customInsertCallable;
		private ExecuteUpdateResultCheckStyle insertCheckStyle;

		private SqlString customSQLDelete;
		private bool customDeleteCallable;
		private ExecuteUpdateResultCheckStyle deleteCheckStyle;

		private SqlString customSQLUpdate;
		private bool customUpdateCallable;
		private ExecuteUpdateResultCheckStyle updateCheckStyle;

		private string loaderName;

		private bool? isAbstract;

		protected readonly ISet<string> synchronizedTables = new HashedSet<string>();
		private bool hasSubselectLoadableCollections;
		private string entityName;

		private IDictionary<EntityMode, System.Type> tuplizerImpls;

		/// <summary>
		/// Gets or Sets if the Insert Sql is built dynamically.
		/// </summary>
		/// <value><see langword="true" /> if the Sql is built at runtime.</value>
		/// <remarks>
		/// The value of this is set by the <c>dynamic-insert</c> attribute. 
		/// </remarks>
		public virtual bool DynamicInsert
		{
			get { return dynamicInsert; }
			set { dynamicInsert = value; }
		}

		/// <summary>
		/// Gets or Sets if the Update Sql is built dynamically.
		/// </summary>
		/// <value><see langword="true" /> if the Sql is built at runtime.</value>
		/// <remarks>
		/// The value of this is set by the <c>dynamic-update</c> attribute. 
		/// </remarks>
		public virtual bool DynamicUpdate
		{
			get { return dynamicUpdate; }
			set { dynamicUpdate = value; }
		}

		/// <summary>
		/// Gets or Sets the value to use as the discriminator for the Class.
		/// </summary>
		/// <value>
		/// A value that distinguishes this subclass in the database.
		/// </value>
		/// <remarks>
		/// The value of this is set by the <c>discriminator-value</c> attribute.  Each <c>&lt;subclass&gt;</c>
		/// in a heirarchy must define a unique <c>discriminator-value</c>.  The default value 
		/// is the class name if no value is supplied.
		/// </remarks>
		public virtual string DiscriminatorValue
		{
			get { return discriminatorValue; }
			set { discriminatorValue = value; }
		}

		/// <summary>
		/// Adds a <see cref="Subclass"/> to the class hierarchy.
		/// </summary>
		/// <param name="subclass">The <see cref="Subclass"/> to add to the hierarchy.</param>
		public virtual void AddSubclass(Subclass subclass)
		{
			// Inheritable cycle detection (paranoid check)
			PersistentClass superclass = Superclass;
			while (superclass != null)
			{
				if (subclass.Name == superclass.Name)
				{
					throw new MappingException(
						string.Format("Circular inheritance mapping detected: {0} will have itself as superclass when extending {1}",
						              subclass.Name, Name));
				}
				superclass = superclass.Superclass;
			}
			subclasses.Add(subclass);
		}

		/// <summary>
		/// Gets a boolean indicating if this PersistentClass has any subclasses.
		/// </summary>
		/// <value><see langword="true" /> if this PeristentClass has any subclasses.</value>
		public virtual bool HasSubclasses
		{
			get { return subclasses.Count > 0; }
		}

		/// <summary>
		/// Gets the number of subclasses that inherit either directly or indirectly.
		/// </summary>
		/// <value>The number of subclasses that inherit from this PersistentClass.</value>
		public virtual int SubclassSpan
		{
			get
			{
				int n = subclasses.Count;
				foreach (Subclass sc in subclasses)
				{
					n += sc.SubclassSpan;
				}
				return n;
			}
		}

		/// <summary>
		/// Iterate over subclasses in a special 'order', most derived subclasses first.
		/// </summary>
		/// <value>
		/// It will recursively go through Subclasses so that if a Subclass has Subclasses
		/// it will pick those up also.
		/// </value>
		public virtual IEnumerable<Subclass> SubclassIterator
		{
			get
			{
				IEnumerable<Subclass>[] iters = new IEnumerable<Subclass>[subclasses.Count + 1];
				int i = 0;
				foreach (Subclass subclass in subclasses)
				{
					iters[i++] = subclass.SubclassIterator;
				}
				iters[i] = subclasses;
				return new JoinedEnumerable<Subclass>(iters);
			}
		}

		/// <summary>
		/// Gets an <see cref="IEnumerable"/> of <see cref="Subclass"/> objects
		/// that directly inherit from this PersistentClass.
		/// </summary>
		/// <value>
		/// An <see cref="IEnumerable"/> of <see cref="Subclass"/> objects
		/// that directly inherit from this PersistentClass.
		/// </value>
		public virtual IEnumerable<Subclass> DirectSubclasses
		{
			get { return subclasses; }
		}

		public virtual string EntityName
		{
			get{return entityName;}
			set{entityName = value;}
		}
		/// <summary>
		/// Change the property definition or add a new property definition
		/// </summary>
		/// <param name="p">The <see cref="Property"/> to add.</param>
		public virtual void AddProperty(Property p)
		{
			properties.Add(p);
			p.PersistentClass = this;
		}

		/// <summary>
		/// Gets or Sets the <see cref="Table"/> that this class is stored in.
		/// </summary>
		/// <value>The <see cref="Table"/> this class is stored in.</value>
		/// <remarks>
		/// The value of this is set by the <c>table</c> attribute. 
		/// </remarks>
		public abstract Table Table { get; }

		public virtual int PropertyClosureSpan
		{
			get
			{
				int span = properties.Count;
				foreach (Join join in joins)
				{
					span += join.PropertySpan;
				}
				return span;
			}
		}

		public virtual int GetJoinNumber(Property prop)
		{
			int result = 1;
			foreach (Join join in SubclassJoinClosureIterator)
			{
				if (join.ContainsProperty(prop))
					return result;
				result++;
			}
			return 0;
		}

		/// <summary>
		/// Gets an <see cref="IEnumerable"/> of <see cref="Property"/> objects.
		/// </summary>
		/// <value>
		/// An <see cref="IEnumerable"/> of <see cref="Property"/> objects.
		/// </value>
		public virtual IEnumerable<Property> PropertyIterator
		{
			get
			{
				List<IEnumerable<Property>> iterators = new List<IEnumerable<Property>>();
				iterators.Add(properties);
				foreach (Join join in joins)
				{
					iterators.Add(join.PropertyIterator);
				}
				return new JoinedEnumerable<Property>(iterators);
			}
		}

		/// <summary>
		/// Gets the <see cref="System.Type"/> that is being mapped.
		/// </summary>
		/// <value>The <see cref="System.Type"/> that is being mapped.</value>
		/// <remarks>
		/// The value of this is set by the <c>name</c> attribute on the <c>&lt;class&gt;</c> 
		/// element.
		/// </remarks>
		public virtual System.Type MappedClass
		{
			get { return mappedClass; }
			set { mappedClass = value; }
		}

		/// <summary>
		/// Gets the fully qualified name of the type being persisted.
		/// </summary>
		/// <value>The fully qualified name of the type being persisted.</value>
		public virtual string Name
		{
			get { return mappedClass.FullName; }
		}

		/// <summary>
		/// When implemented by a class, gets or set a boolean indicating 
		/// if the mapped class has properties that can be changed.
		/// </summary>
		/// <value><see langword="true" /> if the object is mutable.</value>
		/// <remarks>
		/// The value of this is set by the <c>mutable</c> attribute. 
		/// </remarks>
		public abstract bool IsMutable { get; set; }

		/// <summary>
		/// When implemented by a class, gets a boolean indicating
		/// if the mapped class has a Property for the <c>id</c>.
		/// </summary>
		/// <value><see langword="true" /> if there is a Property for the <c>id</c>.</value>
		public abstract bool HasIdentifierProperty { get; }

		/// <summary>
		/// When implemented by a class, gets or sets the <see cref="Property"/>
		/// that is used as the <c>id</c>.
		/// </summary>
		/// <value>
		/// The <see cref="Property"/> that is used as the <c>id</c>.
		/// </value>
		public abstract Property IdentifierProperty { get; set; }

		/// <summary>
		/// When implemented by a class, gets or sets the <see cref="SimpleValue"/>
		/// that contains information about the identifier.
		/// </summary>
		/// <value>The <see cref="SimpleValue"/> that contains information about the identifier.</value>
		public abstract SimpleValue Identifier { get; set; }

		/// <summary>
		/// When implemented by a class, gets or sets the <see cref="Property"/>
		/// that is used as the version.
		/// </summary>
		/// <value>The <see cref="Property"/> that is used as the version.</value>
		public abstract Property Version { get; set; }

		/// <summary>
		/// When implemented by a class, gets or sets the <see cref="SimpleValue"/>
		/// that contains information about the discriminator.
		/// </summary>
		/// <value>The <see cref="SimpleValue"/> that contains information about the discriminator.</value>
		public abstract SimpleValue Discriminator { get; set; }

		/// <summary>
		/// When implemented by a class, gets a boolean indicating if this
		/// mapped class is inherited from another. 
		/// </summary>
		/// <value>
		/// <see langword="true" /> if this class is a <c>subclass</c> or <c>joined-subclass</c>
		/// that inherited from another <c>class</c>.
		/// </value>
		public abstract bool IsInherited { get; }

		/// <summary>
		/// When implemented by a class, gets or sets if the mapped class has subclasses or is
		/// a subclass.
		/// </summary>
		/// <value>
		/// <see langword="true" /> if the mapped class has subclasses or is a subclass.
		/// </value>
		public abstract bool IsPolymorphic { get; set; }

		/// <summary>
		/// When implemented by a class, gets a boolean indicating if the mapped class
		/// has a version property.
		/// </summary>
		/// <value><see langword="true" /> if there is a <c>&lt;version&gt;</c> property.</value>
		public abstract bool IsVersioned { get; }

		/// <summary>
		/// When implemented by a class, gets or sets the CacheConcurrencyStrategy
		/// to use to read/write instances of the persistent class to the Cache.
		/// </summary>
		/// <value>The CacheConcurrencyStrategy used with the Cache.</value>
		public abstract string CacheConcurrencyStrategy { get; set; }

		/// <summary>
		/// When implemented by a class, gets or sets the <see cref="PersistentClass"/>
		/// that this mapped class is extending.
		/// </summary>
		/// <value>
		/// The <see cref="PersistentClass"/> that this mapped class is extending.
		/// </value>
		public abstract PersistentClass Superclass { get; set; }

		/// <summary>
		/// When implemented by a class, gets or sets a boolean indicating if 
		/// explicit polymorphism should be used in Queries.
		/// </summary>
		/// <value>
		/// <see langword="true" /> if only classes queried on should be returned, <see langword="false" />
		/// if any class in the heirarchy should implicitly be returned.</value>
		/// <remarks>
		/// The value of this is set by the <c>polymorphism</c> attribute. 
		/// </remarks>
		public abstract bool IsExplicitPolymorphism { get; set; }

		/// <summary>
		/// When implemented by a class, gets an <see cref="IEnumerable"/> 
		/// of <see cref="Property"/> objects that this mapped class contains.
		/// </summary>
		/// <value>
		/// An <see cref="IEnumerable"/> of <see cref="Property"/> objects that 
		/// this mapped class contains.
		/// </value>
		/// <remarks>
		/// This is all of the properties of this mapped class and each mapped class that
		/// it is inheriting from.
		/// </remarks>
		public abstract IEnumerable<Property> PropertyClosureIterator { get; }

		/// <summary>
		/// When implemented by a class, gets an <see cref="IEnumerable"/> 
		/// of <see cref="Table"/> objects that this mapped class reads from
		/// and writes to.
		/// </summary>
		/// <value>
		/// An <see cref="IEnumerable"/> of <see cref="Table"/> objects that 
		/// this mapped class reads from and writes to.
		/// </value>
		/// <remarks>
		/// This is all of the tables of this mapped class and each mapped class that
		/// it is inheriting from.
		/// </remarks>
		public abstract IEnumerable<Table> TableClosureIterator { get; }

		/// <summary>
		/// Adds a <see cref="Property"/> that is implemented by a subclass.
		/// </summary>
		/// <param name="p">The <see cref="Property"/> implemented by a subclass.</param>
		public virtual void AddSubclassProperty(Property p)
		{
			subclassProperties.Add(p);
		}

		public virtual void AddSubclassJoin(Join join)
		{
			subclassJoins.Add(join);
		}

		/// <summary>
		/// Adds a <see cref="Table"/> that a subclass is stored in.
		/// </summary>
		/// <param name="table">The <see cref="Table"/> the subclass is stored in.</param>
		public virtual void AddSubclassTable(Table table)
		{
			subclassTables.Add(table);
		}

		/// <summary>
		/// Gets an <see cref="IEnumerable"/> of <see cref="Property"/> objects that
		/// this mapped class contains and that all of its subclasses contain.
		/// </summary>
		/// <value>
		/// An <see cref="IEnumerable"/> of <see cref="Property"/> objects that
		/// this mapped class contains and that all of its subclasses contain.
		/// </value>
		public virtual IEnumerable<Property> SubclassPropertyClosureIterator
		{
			get
			{
				List<IEnumerable<Property>> iters = new List<IEnumerable<Property>>();
				iters.Add(PropertyClosureIterator);
				iters.Add(subclassProperties);
				foreach (Join join in subclassJoins)
				{
					iters.Add(join.PropertyIterator);
				}
				return new JoinedEnumerable<Property>(iters);
			}
		}

		public virtual IEnumerable<Join> SubclassJoinClosureIterator
		{
			get { return new JoinedEnumerable<Join>(JoinClosureIterator, subclassJoins); }
		}

		/// <summary>
		/// Gets an <see cref="IEnumerable"/> of all of the <see cref="Table"/> objects that the 
		/// subclass finds its information in.  
		/// </summary>
		/// <value>An <see cref="IEnumerable"/> of <see cref="Table"/> objects.</value>
		/// <remarks>It adds the TableClosureIterator and the subclassTables into the IEnumerable.</remarks>
		public virtual IEnumerable<Table> SubclassTableClosureIterator
		{
			get { return new JoinedEnumerable<Table>(TableClosureIterator, subclassTables); }
		}

		public virtual bool IsClassOrSuperclassJoin(Join join)
		{
			return joins.Contains(join);
		}

		public virtual bool IsClassOrSuperclassTable(Table closureTable)
		{
			return Table == closureTable;
		}

		/// <summary>
		/// Gets or sets the <see cref="System.Type"/> to use as a Proxy.
		/// </summary>
		/// <value>The <see cref="System.Type"/> to use as a Proxy.</value>
		/// <remarks>
		/// The value of this is set by the <c>proxy</c> attribute. 
		/// </remarks>
		public virtual System.Type ProxyInterface
		{
			get { return proxyInterface; }
			set { proxyInterface = value; }
		}

		/// <summary>
		/// Gets or sets a boolean indicating if only values in the discriminator column that
		/// are mapped will be included in the sql.
		/// </summary>
		/// <value><see langword="true" /> if the mapped discriminator values should be forced.</value>
		/// <remarks>
		/// The value of this is set by the <c>force</c> attribute on the <c>discriminator</c> element. 
		/// </remarks>
		public virtual bool IsForceDiscriminator
		{
			get { return false; }
			set { throw new NotImplementedException("subclasses need to override this method"); }
		}

		/// <summary>
		/// 
		/// </summary>
		public bool IsDiscriminatorValueNotNull
		{
			get { return NotNullDiscriminatorMapping.Equals(DiscriminatorValue); }
		}

		/// <summary>
		/// 
		/// </summary>
		public bool IsDiscriminatorValueNull
		{
			get { return NullDiscriminatorMapping.Equals(DiscriminatorValue); }
		}

		public IDictionary<string, MetaAttribute> MetaAttributes
		{
			get { return metaAttributes; }
			set { metaAttributes = value; }
		}

		public MetaAttribute GetMetaAttribute(string name)
		{
			return metaAttributes[name];
		}

		public virtual IEnumerable<Join> JoinIterator
		{
			get { return joins; }
		}

		public virtual IEnumerable<Join> JoinClosureIterator
		{
			get { return joins; }
		}

		public virtual void AddJoin(Join join)
		{
			joins.Add(join);
			join.PersistentClass = this;
		}

		public virtual int JoinClosureSpan
		{
			get { return joins.Count; }
		}

		public bool IsLazy
		{
			get { return lazy; }
			set { lazy = value; }
		}

		/// <summary>
		/// When implemented by a class, gets or sets a boolean indicating if the identifier is 
		/// embedded in the class.
		/// </summary>
		/// <value><see langword="true" /> if the class identifies itself.</value>
		/// <remarks>
		/// An embedded identifier is true when using a <c>composite-id</c> specifying
		/// properties of the class as the <c>key-property</c> instead of using a class
		/// as the <c>composite-id</c>.
		/// </remarks>
		public abstract bool HasEmbeddedIdentifier { get; set; }

		/// <summary>
		/// When implemented by a class, gets or sets the <see cref="System.Type"/> of the Persister.
		/// </summary>
		public abstract System.Type ClassPersisterClass { get; set; }

		/// <summary>
		/// When implemented by a class, gets the <see cref="Table"/> of the class
		/// that is mapped in the <c>class</c> element.
		/// </summary>
		/// <value>
		/// The <see cref="Table"/> of the class that is mapped in the <c>class</c> element.
		/// </value>
		public abstract Table RootTable { get; }

		/// <summary>
		/// When implemented by a class, gets the <see cref="RootClass"/> of the class
		/// that is mapped in the <c>class</c> element.
		/// </summary>
		/// <value>
		/// The <see cref="RootClass"/> of the class that is mapped in the <c>class</c> element.
		/// </value>
		public abstract RootClass RootClazz { get; }

		/// <summary>
		/// When implemented by a class, gets or sets the <see cref="SimpleValue"/>
		/// that contains information about the Key.
		/// </summary>
		/// <value>The <see cref="SimpleValue"/> that contains information about the Key.</value>
		public abstract IKeyValue Key { get; set; }

		/// <summary>
		/// Creates the <see cref="PrimaryKey"/> for the <see cref="Table"/>
		/// this type is persisted in.
		/// </summary>
		/// <param name="dialect">The <see cref="Dialect.Dialect"/> that is used to Alias columns.</param>
		public virtual void CreatePrimaryKey(Dialect.Dialect dialect)
		{
			PrimaryKey pk = new PrimaryKey();
			Table table = Table;
			pk.Table = table;
			pk.Name = PKAlias.ToAliasString(table.Name, dialect);
			table.PrimaryKey = pk;

			foreach (Column col in Key.ColumnIterator)
			{
				pk.AddColumn(col);
			}
		}

		/// <summary>
		/// 
		/// </summary>
		public int BatchSize
		{
			get { return batchSize; }
			set { batchSize = value; }
		}

		/// <summary>
		/// 
		/// </summary>
		public bool SelectBeforeUpdate
		{
			get { return selectBeforeUpdate; }
			set { selectBeforeUpdate = value; }
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="propertyName"></param>
		/// <returns></returns>
		public Property GetProperty(string propertyName)
		{
			return GetProperty(propertyName, PropertyClosureIterator);
		}

		private Property GetProperty(string propertyName, IEnumerable<Property> iter)
		{
			foreach (Property prop in iter)
			{
				if (prop.Name.Equals(propertyName))
				{
					return prop;
				}
			}
			throw new MappingException(string.Format("property not found: {0} on entity {1}", propertyName, Name));
		}

		public OptimisticLockMode OptimisticLockMode
		{
			get { return optimisticLockMode; }
			set { optimisticLockMode = value; }
		}

		/// <summary>
		/// When implemented by a class, gets or sets the sql string that should 
		/// be a part of the where clause.
		/// </summary>
		/// <value>
		/// The sql string that should be a part of the where clause.
		/// </value>
		/// <remarks>
		/// The value of this is set by the <c>where</c> attribute. 
		/// </remarks>
		public abstract string Where { get; set; }

		public override string ToString()
		{
			return GetType() + " for " + MappedClass;
		}

		/// <summary>
		/// 
		/// </summary>
		public abstract bool IsJoinedSubclass { get; }

		/// <summary>
		/// 
		/// </summary>
		public abstract bool IsDiscriminatorInsertable { get; set; }

		/// <summary>
		/// 
		/// </summary>
		/// <param name="mapping"></param>
		public virtual void Validate(IMapping mapping)
		{
			foreach (Property prop in PropertyIterator)
			{
				if (!prop.IsValid(mapping))
				{
					throw new MappingException(
						string.Format("property mapping has wrong number of columns: {0} type: {1}",
						              StringHelper.Qualify(MappedClass.Name, Name), prop.Type.Name));
				}
			}
		}

		public bool HasPocoRepresentation
		{
			get { return true; }
		}

		public bool? IsAbstract
		{
			get { return isAbstract; }
			set { isAbstract = value; }
		}

		public Property GetRecursiveProperty(string propertyPath)
		{
			return GetRecursiveProperty(propertyPath, PropertyIterator);
		}

		private Property GetRecursiveProperty(string propertyPath, IEnumerable<Property> iter)
		{
			Property property = null;

			StringTokenizer st = new StringTokenizer(propertyPath, ".", false);
			try
			{
				foreach (string element in st)
				{
					if (property == null)
					{
						property = GetProperty(element, iter);
					}
					else
					{
						//flat recursive algorithm
						property = ((Component) property.Value).GetProperty(element);
					}
				}
			}
			catch (MappingException e)
			{
				throw new MappingException(
					"property not found: " + propertyPath +
					" in entity: " + Name, e
					);
			}

			return property;
		}

		public string LoaderName
		{
			get { return loaderName; }
			set { loaderName = value; }
		}

		public SqlString CustomSQLInsert
		{
			get { return customSQLInsert; }
		}

		public SqlString CustomSQLDelete
		{
			get { return customSQLDelete; }
		}

		public SqlString CustomSQLUpdate
		{
			get { return customSQLUpdate; }
		}

		public bool IsCustomInsertCallable
		{
			get { return customInsertCallable; }
		}

		public bool IsCustomDeleteCallable
		{
			get { return customDeleteCallable; }
		}

		public bool IsCustomUpdateCallable
		{
			get { return customUpdateCallable; }
		}

		public ExecuteUpdateResultCheckStyle CustomSQLInsertCheckStyle
		{
			get { return insertCheckStyle; }
		}

		public ExecuteUpdateResultCheckStyle CustomSQLDeleteCheckStyle
		{
			get { return deleteCheckStyle; }
		}

		public ExecuteUpdateResultCheckStyle CustomSQLUpdateCheckStyle
		{
			get { return updateCheckStyle; }
		}

		public void SetCustomSQLInsert(string sql, bool callable, ExecuteUpdateResultCheckStyle checkStyle)
		{
			customSQLInsert = SqlString.Parse(sql);
			customInsertCallable = callable;
			insertCheckStyle = checkStyle;
		}

		public void SetCustomSQLDelete(string sql, bool callable, ExecuteUpdateResultCheckStyle checkStyle)
		{
			customSQLDelete = SqlString.Parse(sql);
			customDeleteCallable = callable;
			deleteCheckStyle = checkStyle;
		}

		public void SetCustomSQLUpdate(string sql, bool callable, ExecuteUpdateResultCheckStyle checkStyle)
		{
			customSQLUpdate = SqlString.Parse(sql);
			customUpdateCallable = callable;
			updateCheckStyle = checkStyle;
		}

		public abstract ISet<string> SynchronizedTables { get; }

		public void AddSynchronizedTable(string table)
		{
			synchronizedTables.Add(table);
		}

		public void AddFilter(string name, string condition)
		{
			filters.Add(name, condition);
		}

		public virtual IDictionary<string,string> FilterMap
		{
			get { return filters; }
		}

		public virtual bool HasSubselectLoadableCollections
		{
			get { return hasSubselectLoadableCollections; }
			set { hasSubselectLoadableCollections = value; }
		}

		internal abstract int NextSubclassId();

		public abstract int SubclassId { get; }

		/// <summary>
		/// Given a property path, locate the appropriate referenceable property reference.
		/// </summary>
		/// <remarks>
		/// A referenceable property is a property  which can be a target of a foreign-key
		/// mapping (an identifier or explicitly named in a property-ref).
		/// </remarks>
		/// <param name="propertyPath">The property path to resolve into a property reference.</param>
		/// <returns>The property reference (never null).</returns>
		/// <exception cref="MappingException">If the property could not be found.</exception>
		public Property GetReferencedProperty(string propertyPath)
		{
			try
			{
				return GetRecursiveProperty(propertyPath, ReferenceablePropertyIterator);
			}
			catch (MappingException e)
			{
				throw new MappingException(
					"property-ref [" + propertyPath + "] not found on entity [" + MappedClass + "]", e
					);
			}
		}

		/// <summary>
		/// Build a collection of properties which are "referenceable".
		/// </summary>
		/// <remarks>
		/// See <see cref="GetReferencedProperty"/> for a discussion of "referenceable".
		/// </remarks>
		public virtual IEnumerable<Property> ReferenceablePropertyIterator
		{
			get { return PropertyClosureIterator; }
		}

		public abstract bool IsLazyPropertiesCacheable { get;}

		protected internal virtual IEnumerable<Property> NonDuplicatedPropertyIterator
		{
			get { return UnjoinedPropertyIterator; }
		}

		/// <summary> 
		/// Build an enumerable over the properties defined on this class <b>which
		/// are not defined as part of a join</b>.  
		/// As with <see cref="PropertyIterator"/> the returned iterator only accounts 
		/// for non-identifier properties.
		/// </summary>
		/// <returns> An enumerable over the non-joined "normal" properties.</returns>
		public virtual IEnumerable<Property> UnjoinedPropertyIterator
		{
			get { return properties; }
		}

		public virtual Table IdentityTable
		{
			get { return RootTable; }
		}

		public virtual IEnumerable<PersistentClass> SubclassClosureIterator
		{
			get
			{
				List<IEnumerable<PersistentClass>> iters = new List<IEnumerable<PersistentClass>>();
				iters.Add(new SingletonEnumerable<PersistentClass>(this));
				foreach (Subclass clazz in SubclassIterator)
				{
					iters.Add(clazz.SubclassClosureIterator);
				}
				return new JoinedEnumerable<PersistentClass>(iters);
			}

		}

		public virtual Component IdentifierMapper
		{
			get { return identifierMapper; }
			set { identifierMapper = value; }
		}

		public virtual bool HasIdentifierMapper
		{
			get { return identifierMapper != null; }
		}

		public void AddTuplizer(EntityMode entityMode, System.Type implClass)
		{
			if (tuplizerImpls == null)
			{
				tuplizerImpls = new Dictionary<EntityMode, System.Type>();
			}
			tuplizerImpls[entityMode] = implClass;
		}

		public virtual System.Type GetTuplizerImplClassName(EntityMode mode)
		{
			if (tuplizerImpls == null)
				return null;
			return tuplizerImpls[mode];
		}

		public virtual IDictionary<EntityMode, System.Type> TuplizerMap
		{
			get
			{
				if (tuplizerImpls == null)
					return null;

				return new Dictionary<EntityMode, System.Type>(tuplizerImpls);
			}

		}
	}
}
