/////////////////////////////////////////////////////////////////////////*
//Ink Studios Source File.
//Copyright (C), Ink Studios, 2011.
//////////////////////////////////////////////////////////////////////////
// The entity manager handles spawning, removing and storing of mono
// entities.
//////////////////////////////////////////////////////////////////////////
// 21/12/2011 : Created by Filip 'i59' Lundgren
////////////////////////////////////////////////////////////////////////*/
#ifndef __ENTITY_MANAGER_H__
#define __ENTITY_MANAGER_H__

#include "MonoCommon.h"
#include <IMonoArray.h>

#include <IMonoEntityManager.h>
#include <IMonoScriptBind.h>

#include <mono\mini\jit.h>

#include <IEntitySystem.h>
#include <IBreakableManager.h>
#include <IAnimatedCharacter.h>

struct IMonoScript;
class CEntity;

struct MovementRequest;
struct MonoPhysicalizationParams;
struct ActionImpulse;

struct MovementRequest
{
	ECharacterMoveType type;

	Vec3 velocity;
};

struct EntitySpawnParams
{
	mono::string sName;
	mono::string sClass;

	Vec3 pos;
	Vec3 rot;
	Vec3 scale;

	EEntityFlags flags;
};

struct EntityRegisterParams
{
	mono::string Name;
	mono::string Category;

	mono::string EditorHelper;
	mono::string EditorIcon;

	EEntityClassFlags Flags;
};

struct SMonoEntityProperty
{
	mono::string name;
	mono::string description;
	mono::string editType;

	IEntityPropertyHandler::EPropertyType type;
	uint32 flags;

	IEntityPropertyHandler::SPropertyInfo::SLimits limits;
};

struct SMonoEntityInfo
{
	SMonoEntityInfo(IEntity *pEnt)
		: pEntity(pEnt)
	{
		if(pEnt != NULL)
			id = pEnt->GetId();
		else
			id = 0;
	}

	SMonoEntityInfo(IEntity *pEnt, EntityId entId)
		: pEntity(pEnt)
		, id(entId)
	{
	}

	IEntity *pEntity;
	EntityId id;
};

class CEntityManager 
	: public IMonoEntityManager
	, public IMonoScriptBind
	, public IEntitySystemSink
{

public:
	CEntityManager();
	~CEntityManager();

	// IEntitySystemSink
	virtual bool OnBeforeSpawn( SEntitySpawnParams &params );
	virtual void OnSpawn( IEntity *pEntity,SEntitySpawnParams &params );
	virtual bool OnRemove( IEntity *pEntity );
	virtual void OnReused( IEntity *pEntity, SEntitySpawnParams &params ) {}
	virtual void OnEvent( IEntity *pEntity, SEntityEvent &event ) {}
	// ~IEntitySystemSink

	// IMonoEntityManager
	virtual IMonoClass *GetScript(EntityId entityId, bool returnBackIfInvalid = false) override;
	// ~IMonoEntityManager

	std::shared_ptr<CEntity> GetMonoEntity(EntityId entityId);
	std::shared_ptr<CEntity> GetMonoEntity(EntityGUID entityGUID);
	bool IsMonoEntity(const char *entityClassName);

protected:
	// IMonoScriptBind
	virtual const char *GetClassName() { return "EntityBase"; }
	// ~IMonoScriptBind

	// ScriptBinds
	static bool SpawnEntity(EntitySpawnParams, bool, SMonoEntityInfo &entityInfo);
	static void RemoveEntity(EntityId);

	static IEntity *GetEntity(EntityId id);

	static bool RegisterEntityClass(EntityRegisterParams, mono::array);

	static EntityId FindEntity(mono::string);
	static mono::array GetEntitiesByClass(mono::string);
	static mono::array GetEntitiesInBox(AABB bbox, int objTypes);

	static mono::string GetPropertyValue(IEntity *pEnt, mono::string);

	static void SetWorldPos(IEntity *pEnt, Vec3);
	static Vec3 GetWorldPos(IEntity *pEnt);
	static void SetPos(IEntity *pEnt, Vec3);
	static Vec3 GetPos(IEntity *pEnt);

	static void SetWorldRotation(IEntity *pEnt, Quat);
	static Quat GetWorldRotation(IEntity *pEnt);
	static void SetRotation(IEntity *pEnt, Quat);
	static Quat GetRotation(IEntity *pEnt);

	static AABB GetBoundingBox(IEntity *pEnt);
	static AABB GetWorldBoundingBox(IEntity *pEnt);

	static void LoadObject(IEntity *pEnt, mono::string, int);
	static void LoadCharacter(IEntity *pEnt, mono::string, int);

	static EEntitySlotFlags GetSlotFlags(IEntity *pEnt, int);
	static void SetSlotFlags(IEntity *pEnt, int, EEntitySlotFlags);

	static void BreakIntoPieces(IEntity *pEnt, int, int, IBreakableManager::BreakageParams);

	static void CreateGameObjectForEntity(IEntity *pEnt);
	static void BindGameObjectToNetwork(IEntity *pEnt);

	static mono::string GetStaticObjectFilePath(IEntity *pEnt, int);

	static void SetWorldTM(IEntity *pEnt, Matrix34 tm);
	static Matrix34 GetWorldTM(IEntity *pEnt);
	static void SetLocalTM(IEntity *pEnt, Matrix34 tm);
	static Matrix34 GetLocalTM(IEntity *pEnt);

	static mono::string GetName(IEntity *pEnt);
	static void SetName(IEntity *pEnt, mono::string name);

	static EEntityFlags GetFlags(IEntity *pEnt);
	static void SetFlags(IEntity *pEnt, EEntityFlags flags);

	static int GetAttachmentCount(IEntity *pEnt);
	static IMaterial *GetAttachmentMaterialByIndex(IEntity *pEnt, int index);
	static void SetAttachmentMaterialByIndex(IEntity *pEnt, int index, IMaterial *pMaterial);

	static IMaterial *GetAttachmentMaterial(IEntity *pEnt, mono::string name);
	static void SetAttachmentMaterial(IEntity *pEnt, mono::string attachmentName, IMaterial *pMaterial);
	/// End direct entity calls

	// ~ScriptBinds
	
	typedef std::vector<std::shared_ptr<CEntity>> TMonoEntities;
	static TMonoEntities m_monoEntities;

	static std::vector<const char *> m_monoEntityClasses;
};

#endif //__ENTITY_MANAGER_H__