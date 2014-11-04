# Base Entities

Signum Framework provides a clear hierarchy of classes that serve as base classes for your own entities: 

* **Modifiable:** Base class with embedded change tracking.
  * **[MList\<T>](MList.md):** Similar to `List<T>` but with embedded change tracking.
  * **[Lite\<T>](Lite.md):** Allows lazy relationships and lightweight strongly-typed references to entities.
  * **ModifiableEntity:**: Base class for *entities* with change tracking and validation.  
    * **EmbeddedEntity**: Base class for entities without `Id` that live inside other entities.  
	    * **ModelEntity**: Base class for entities that won't be saved in the database (ViewModels)
    * **IdentifiableEntity**: Base class for entities with `Id` and their own table in the database.
		* **Entity**: Base class for entities that have `Id` and also concurrency control. 
		* **[EnumEntity\<T>](EnumEntity.md):** Represents a `enum` table. 
		* **[Symbol and SemiSymbol](Symbols.md):** Like `enums` but can be declared in different types. 
	* **[MixinEntity](Mixin.md):** Entity witch properties are effectively appended to the end of another `IdentifiableEntity`. 

## Modifiable

At the very root we find the `Modifiable` class, inheriting from `object`. It's not even an entity, in fact it's so abstract that it's a hard to explain. It's the base class for anything that can be saved and provides change tracking. Even `MList<T>` and `Lite<T>` inherit from this `Modifiable`.

Basically `Modifiable` contains the property `Modified` of type `ModifiedState`. 

`Modifiable`  defines the `PreSaving` and `PostRetriving` virtual methods, that will be called just before saving an object and just after retrieving it. 

Also, `Modifiable` has an important role on Entity Graphs. 


## ModifiableEntity

The simplest entity possible. Your entities shouldn't inherit from `ModifiableEntity` directly.

`ModifiableEntity` implements `Modifiable.Modified` by checking if some fields was modified. To do so, it exposes the protected `Set` method.
 

`ModifiableEntity` also implements [IDataErrorInfo](http://msdn.microsoft.com/en-us/library/system.componentmodel.idataerrorinfo.aspx) and provides the basic plumbing for [Validation](Validation.md).

## EmbeddedEntity
Base class to be used when you want an entity to be embedded inside of the holders Entity. Small entities like Interval, SocialSecurityNumber, Color, GpsLocation or Address could inherit from here. 

In the current implementation, this class adds nothing over ModifiableEntity. Instead it's just a marker class to make it easier to remember what to subclass when you want Embedded behavior. 

On the database, embedded fields are stored in the parent entity table. Let's see an example: 

* If a `PersonDN` class has an `EmbeddedEntity` of type `AddressDN` with name `HomeAddress`. 
* And `AddressDN` has a field `Street`.
* Then `PersonDN` table will have a column with name `HomeAddress_Street`. 

Since `EmbeddedEntity` is a classes (reference types), by default they are nullable in the database as well, in order to reduce type-mismatch. This behavior is implemented adding a `HasValue` column and forcing nullability to the remaining embedded fields. 

Most of the time this behavior is unnecessary. You can remove it using `[NotNullableAttribute]` in your `EmbeddedEntity` field. 

## ModelEntity

Model entities are entities not meant to be stored in the database, just used as ViewModels for complex windows/webs that do not map exactly to the database, or temporal dialog that could be passed as a parameter to operations. 

Currently they inherit from `EmbeddedEntity` for simplicity, but they are not an  embedded entity. 

`ModelEntity` also has all the powerful validation/change-notification/change-tracking features from `ModifiableEntity`-  


## IdentifiableEntity

This is the basic entity with Identity. It has the right to have its own table. It also:

* Defines the `Id` field of type int to be the primary key. The property throws a `InvalidOperationExeption` if the entity is null.
* Defines the `IdOrNull` property of type `int?` witch return null if the entity is new.
* Defines the `IsNew` property that returns `true` when the entity is new.
* Defines `ToStringProperty` that evaluates `ToString` bus can be invalidated. Useful for binding.
* Generates `ToStr` column with the evaluation of `ToString` before saving if `ToStringExpression` is not defined.
* Overrides `Equals` and `GetHashCode` to depend on the `Id` and `Type`, not in reference equality. 
* Is the basic container of `Mixins`. 

Apart from these features, it implements the `IIdentifiable` interface, which is just a marker interface in case you want to use `ImplementedBy` or `ImplmentedByAll` over interfaces. See more about [Inheritance](Inheritance.md). 

This class is designed to be the base class of simple types with strong identity semantics, like [Enums](EnumEntity.md), [Symbols](Symbols.md) or your own run-time modifiable enumerated types: TypeOfCustomer, Contry, State, etc... because these classes don't have concurrency problems (they are rarely modified) and they don't have [MList\<T>](MList.md). 

Classes inheriting from `IdentifiableEntity` also need to provide and [EntityKindAttribute](EntityKind.md).

### IIdentifiable interface

This interface is only implemented by `IdentifiableEntity` and should be inherited by any interface that will be used by Polymorphic Foreign Key. For example: 

```C#
public interface IProcessDataDN : IIdentifiable
{
}
```

By using an interface inheriting from `IIdentifiable`, instead of a class inheriting from `IdentifiableEntity`, implementers are free to inherit from the class they want. 

## Entity
Finally, the Entity class is a strong `IdentifiableEntity` with concurrency control support. This entity is meant to be the base class for most of your entities (i.e. Employee, Customer, Company...)

We achieve concurrency control by having a `Ticks` field that stores the current version of the entity. The actual value is just `DateTime.Now.Ticks` of the moment the `Transaction` started, so it is the same value for all the entities created or modified in the same transaction. 

Each time we save an entity we also update the `Ticks` value.

Also, while saving a modified entity, we test if the `Ticks` value of the entity is not the same as the one in the database. If that would happen, an exception will be thrown and the transaction will be rollbacked.

Additionally, when modifying a `MList<T>` only the necessary commands (INSERT/DELETE/UPDATE) are sent to the database. Applying this changes to an entity different than the one in-memory will create a corrupt state, that's why **MList\<T> fields can only be part of entities inheriting from Entity**




