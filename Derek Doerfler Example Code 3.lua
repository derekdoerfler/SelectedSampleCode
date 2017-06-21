--[[	
	This file contains a few of the more interesting functions written for the Dota 2 custom game mode
	"Dota Detuned" (https://steamcommunity.com/sharedfiles/filedetails/?id=433203584),	which consists of over 
	10,000 lines of Lua code in total.
	
	The code selected is intended to demonstrate the kind of programming required for such an 
	endeavor, as well as the strengths and limitations of the language and the developer-provided API 
	(found at https://developer.valvesoftware.com/wiki/Dota_2_Workshop_Tools/Scripting/API).
	
	Also, I wanted to mention that I contributed a sizeable amount of this project's code--as well as "Retro Dota"'s
	code--to the SpellLibrary (https://github.com/Pizzalol/SpellLibrary), which is an open-source repository containing
	rewrites of Dota 2's stock abilities and items.

	For this project, I also created custom particle effects and animations for certain abilities and characters, which
	are sometimes referenced in the code below.
]]


--[[
	This function implements a game mechanic that causes a character (named "Sniper")
	to deal more damage when attacking targets that are far away from him, and less damage when
	attacking targets that are close to him.  The maximum and minimum damage percentages vary 
	depending on the level of the ability (which is named "Headshot"), but the mean (100%) 
	damage is always dealt at the midpoint of the character's attack range.
]]
--[[ ============================================================================================================
	Name: Sniper Headshot On Attack
	Pre: This function is called whenever the character begins his attack animation in preparation for an autoattack.
	Parameters: keys, which is a table containing:
		- caster, the unit that has this ability (i.e. Sniper, at least usually)
		- target, the unit that is going to be attacked
		- minimum_damage_percent, the percentage of normal damage that would be dealt if the target is at the same location as the caster
		- maximum_damage_percent, the percentage of normal damage that would be dealt if the target is at the caster's maximum attack range
	Post: Any previous outgoing damage modifiers on the caster are replaced with a new one depending on the distance 
		between the caster and the target.  Attacking will deal more damage to targets who are far away from him, and
		less damage to targets who are close to him.  100% damage is dealt at the midpoint of the caster's attack range,
		and maximum damage is achieved at his maximum attack range.
================================================================================================================= ]]
function Sniper_Headshot_On_Attack(keys)
	--Remove any previously existing relevant modifiers from the caster to make room for the new one.
	if keys.caster.active_headshot_modifier_name ~= nil and keys.caster:HasModifier(keys.caster.active_headshot_modifier_name) then
		keys.caster:RemoveModifierByName(keys.caster.active_headshot_modifier_name)
	end
	
	local caster_attack_range = keys.caster:GetAttackRange()
	local distance = keys.caster:GetRangeToUnit(keys.target)
	local distance_ratio = distance / caster_attack_range  --This calculation will yield a number between 0 and 1 depending on the distance between the caster and the target (with 0 being close and 1 being far).
	
	
	--[[ The equation used to determine the final damage percent will be a quadratic one as follows:
	final_damage_percent = a * (distance_ratio ^ 2) + b * distance_ratio + c
	Calculating a, b, and c is not resource-intensive, so finding them every time this function is called
	will ensure accuracy even when the minimum and maximum damage percentages are frequently changing.
	
	Derivation: We have a series of three known equations, for distance_ratio (i.e. x) == 0, .5, and 1.
	For distance_ratio == 0:    keys.minimum_damage_percent = c
	For distance_ratio == 0.5:  100 = .25a + .5b + c, which is the same as the simpler 400 = a + 2b + 4c
	For distance_ratio == 1:    keys.maximum_damage_percent = a + b + c
	
	Therefore, 
	a = keys.maximum_damage_percent - b - keys.minimum_damage_percent
	400 = keys.maximum_damage_percent + b + (3 * keys.minimum_damage_percent)
	b = 400 - keys.maximum_damage_percent - (3 * keys.minimum_damage_percent)
	a = keys.maximum_damage_percent - (400 - keys.maximum_damage_percent - (3 * keys.minimum_damage_percent)) - keys.minimum_damage_percent
	a = (2 * keys.maximum_damage_percent) + (2 * keys.minimum_damage_percent) - 400
	
	Example min/max damage percentages and their resulting constants are listed below:
	For min/max = 80-125,     a = 10 and b = 35.
	For min/max = 66.67-150,  a = 33.33 and b = 50.
	For min/max = 57.14-175,  a = 64.29 and b = 53.57.
	For min/max = 50-200,     a = 100 and b = 50. 
	]]
	local quadratic_a = (2 * keys.maximum_damage_percent) + (2 * keys.minimum_damage_percent) - 400
	local quadratic_b = 400 - keys.maximum_damage_percent - (3 * keys.minimum_damage_percent)
	local final_damage_percent = quadratic_a * (distance_ratio ^ 2) + quadratic_b * distance_ratio + keys.minimum_damage_percent
	
	
	--Round the damage percent to the nearest integer.
	final_damage_percent = math.floor(final_damage_percent + 0.5)
	
	--Clamp the calculated damage between the minimum and maximum percentages, just to be sure.
	if final_damage_percent > keys.maximum_damage_percent then
		final_damage_percent = keys.maximum_damage_percent
	elseif final_damage_percent < keys.minimum_damage_percent then
		final_damage_percent = keys.minimum_damage_percent
	end
	
	--Store the name of the current modifier that is affecting the caster for the next time this method is called.
	--A major limitation of the modding tools is that some modifier effects cannot have their values changed in Lua code, so a separate modifier must be created for each possible value.
	keys.caster.active_headshot_modifier_name = "sniper_headshot_modifier_damage_percentage_" .. final_damage_percent
	
	--Sniper's base attack time is 1.7, so at the minimum -80% attack speed, it takes him 8.5 seconds (5 times 1.7) to autoattack.
	--The modifier therefore needs to last at least that long; I set it to last 10 seconds just to be safe.
	giveUnitDataDrivenModifier(keys.caster, keys.caster, keys.caster.active_headshot_modifier_name, 10)
end


--[[ ============================================================================================================
	Hero name: Furion
	Ability name: Wrath of Nature
	Effect: Destroys all visible non-mechanical enemy lane creeps on the map.  If the player has an Aghanim's Scepter
		in their inventory, it will destroy all non-mechanical enemy lane creeps, regardless of visibility.
================================================================================================================= ]]
function furion_wrath_of_nature_on_spell_start(keys)
	local playerHero_team = keys.caster:GetTeam()
	local creeplist = nil  --An array to store the enemy creeps on the map that are susceptible to this ability.
	
	if(keys.caster:HasScepter()) then  --Find all non-mechanical enemy lane creeps, regardless of visibility.
		creeplist = FindUnitsInRadius(playerHero_team, Vector(0, 0, 0), nil, FIND_UNITS_EVERYWHERE, DOTA_UNIT_TARGET_TEAM_ENEMY, DOTA_UNIT_TARGET_CREEP, DOTA_UNIT_TARGET_FLAG_NONE, FIND_ANY_ORDER, false)
	else  --Find all visible non-mechanical enemy lane creeps.
		creeplist = FindUnitsInRadius(playerHero_team, Vector(0, 0, 0), nil, FIND_UNITS_EVERYWHERE, DOTA_UNIT_TARGET_TEAM_ENEMY, DOTA_UNIT_TARGET_CREEP, DOTA_UNIT_TARGET_FLAG_FOW_VISIBLE, FIND_ANY_ORDER, false)
	end
	
	for i, individual_creep in ipairs(creeplist) do  --Iterate through all the stored creeps, and kill them.
		if individual_creep:GetName() == "npc_dota_creep_lane" and not individual_creep:IsDominated() and not individual_creep:IsMechanical() then
			ParticleManager:CreateParticle("particles/units/heroes/hero_furion/furion_wrath_of_nature.vpcf",  PATTACH_ABSORIGIN_FOLLOW, individual_creep)
			individual_creep:EmitSound("detuned.furion_wrath_of_nature")
			individual_creep:Kill(keys.ability, keys.caster)
		end
	end
end


--[[ ============================================================================================================
	Hero name: Lion
	Ability name: Earth Spike
	Pre: Called when Earth Spike's projectile hits its target.
	Effect: Stuns and damages with an intensity determined by the difference between the target's current HP and 
		mana percentages.  A great disparity between the two percentages will produce a longer stun but lesser damage;
		a small disparity between the two percentages will produce greater damage but a shorter stun.
================================================================================================================= ]]
function lion_earth_spike_on_projectile_hit_unit(keys)
	local hp_percentage = keys.target:GetHealthPercent()
	local mana_percentage = keys.target:GetManaPercent()

	local damage_to_deal = keys.MinDamage + ((keys.MaxDamage - keys.MinDamage) * (1 - (math.abs(mana_percentage - hp_percentage) / 100)))  --minDamage + (maxDamage - minDamage) * (1 - |manaPercentage - healthPercentage|)
	local stun_duration = keys.MinStun + ((keys.MaxStun - keys.MinStun) * (math.abs(mana_percentage - hp_percentage) / 100))
	
	ApplyDamage({victim = keys.target, attacker = keys.caster, damage = damage_to_deal, damage_type = DAMAGE_TYPE_MAGICAL})
	keys.ability:ApplyDataDrivenModifier(keys.caster, keys.target, "lion_earth_spike_stun_modifier", {Duration = stun_duration})
end


--[[ ============================================================================================================
	Hero name: Lion
	Ability name: Finger of Death
	Effect: Nonlethally swaps the current HP and mana percentages of the targeted allied or enemy hero.
================================================================================================================= ]]
function lion_finger_of_death_on_spell_start(keys)
	local new_hp = (keys.target:GetManaPercent() / 100.00) * keys.target:GetMaxHealth()
	local new_mana = (keys.target:GetHealthPercent() / 100.00) * keys.target:GetMaxMana()
	
	if new_hp < 1 then
		new_hp = 1
	end
	
	--Round the values to the nearest integer.
	keys.target:SetMana(math.floor(new_mana + .5))
	keys.target:SetHealth(math.floor(new_hp + .5))
	
	--Play the particle effect.
	local target_abs_origin = keys.target:GetAbsOrigin()
	local finger_of_death_effect_index = ParticleManager:CreateParticle("particles/units/heroes/hero_lion/lion_spell_finger_of_death.vpcf", PATTACH_ABSORIGIN_FOLLOW, keys.target)
	ParticleManager:SetParticleControl(finger_of_death_effect_index, 1, Vector(target_abs_origin.x, target_abs_origin.y, target_abs_origin.z + ((keys.target:GetBoundingMaxs().z - keys.target:GetBoundingMins().z)/2)))  --Without this, the effect would begin at (0,0,0) rather than on top of the target.
end


--[[ ============================================================================================================
	Item name: Drum of Endurance
	Effect: The caster emits a blast that slowly emanates outwards.  Enemy units it comes in contact with have their 
		movement and attack speed slowed.
	Additional parameters: keys.BlastFinalRadius, keys.BlastSpeedPerSecond, keys.BlastDebuffDuration
================================================================================================================= ]]
function item_drum_of_endurance_on_spell_start(keys)
	local drum_of_endurance_particle = ParticleManager:CreateParticle("particles/items2_fx/shivas_guard_active.vpcf", PATTACH_ABSORIGIN_FOLLOW, keys.caster)
	ParticleManager:SetParticleControlEnt(drum_of_endurance_particle, 0, keys.caster, PATTACH_ABSORIGIN_FOLLOW, "follow_origin", keys.caster:GetAbsOrigin(), false)
	ParticleManager:SetParticleControl(drum_of_endurance_particle, 1, Vector(keys.BlastFinalRadius, keys.BlastFinalRadius / keys.BlastSpeedPerSecond, keys.BlastSpeedPerSecond))
	
	keys.caster:EmitSound("DOTA_Item.ShivasGuard.Activate")
	keys.caster.drum_of_endurance_current_blast_radius = 0
	
	--Every .03 seconds, apply a movement and attack speed debuff to all units within the current radius of the blast (centered around the caster).
	--Stop the timer when the blast has reached its maximum radius.
	Timers:CreateTimer({
		endTime = .03,
		callback = function()			
			keys.caster.drum_of_endurance_current_blast_radius = keys.caster.drum_of_endurance_current_blast_radius + (keys.BlastSpeedPerSecond * .03)  --Expand the blast's radius slightly.
			
			--Find all enemy units located within the current radius.
			local nearby_enemy_units = FindUnitsInRadius(keys.caster:GetTeam(), keys.caster:GetAbsOrigin(), nil, keys.caster.drum_of_endurance_current_blast_radius, DOTA_UNIT_TARGET_TEAM_ENEMY,
			DOTA_UNIT_TARGET_HERO + DOTA_UNIT_TARGET_BASIC, DOTA_UNIT_TARGET_FLAG_NONE, FIND_ANY_ORDER, false)

			for i, individual_unit in ipairs(nearby_enemy_units) do
				if individual_unit:HasModifier("item_drum_of_endurance_modifier_blast_debuff") then  --Temporarily remove existing instances of this debuff so its duration will be refreshed.
					individual_unit:RemoveModifierByNameAndCaster("item_drum_of_endurance_modifier_blast_debuff", keys.caster)
				else  --Play a particle effect if the unit was not already affected by the debuff.
					local drum_of_endurance_impact_particle = ParticleManager:CreateParticle("particles/items2_fx/shivas_guard_impact.vpcf", PATTACH_ABSORIGIN_FOLLOW, individual_unit)
					local target_point = individual_unit:GetAbsOrigin()
					local caster_point = individual_unit:GetAbsOrigin()
					ParticleManager:SetParticleControl(drum_of_endurance_impact_particle, 1, target_point + (target_point - caster_point) * 30)
				end

				keys.ability:ApplyDataDrivenModifier(keys.caster, individual_unit, "item_drum_of_endurance_modifier_blast_debuff", {duration = keys.BlastDebuffDuration})
			end
			
			if keys.caster.drum_of_endurance_current_blast_radius < keys.BlastFinalRadius then
				return .03  --Call the timer again in .03 seconds.
			else  --If the blast has reached or exceeded its intended final radius, stop executing the timer.
				keys.caster.drum_of_endurance_current_blast_radius = 0
				return nil
			end
		end
	})
end


--[[ ============================================================================================================
	Hero name: Queen of Pain
	Ability name: Shadow Strike
	Pre: Called when Shadow Strike's projectile hits an enemy unit.
	Effect: If an enemy unit is hit, the projectile deals base damage plus bonus damage for every Shadow Strike 
		modifier stack already on the unit, then adds a Shadow Strike modifier stack to the unit and refreshes the
		modifier's duration.  If the projectile does not hit anything, or if it hits a new target, existing Shadow
		Strike stacks are removed from the previous target.  If the projectile hits an enemy hero, its entire mana
		cost is refunded.
	Additional parameters: keys.BaseDamage, keys.BonusDamagePerStack, and keys.ManaRefund
================================================================================================================= ]]
function queen_of_pain_shadow_strike_on_projectile_hit_unit(keys)
	--If a new unit has been hit, remove existing modifier stacks from the previous target.
	if keys.caster.shadow_strike_current_target ~= nil and keys.caster.shadow_strike_current_target ~= keys.target and IsValidEntity(keys.caster.shadow_strike_current_target) then
		keys.caster.shadow_strike_current_target:RemoveModifierByNameAndCaster("modifier_queen_of_pain_shadow_strike", keys.caster)
	end
	keys.caster.shadow_strike_current_target = keys.target
	
	--Increment the modifier stack count on the current target.
	local shadow_strike_stack_count = keys.target:GetModifierStackCount("modifier_queen_of_pain_shadow_strike", keys.caster)
	if shadow_strike_stack_count == nil then
		shadow_strike_stack_count = 0
	end
	shadow_strike_stack_count = shadow_strike_stack_count + 1
	
	keys.target:RemoveModifierByNameAndCaster("modifier_queen_of_pain_shadow_strike", keys.caster)  --This is only to refresh the duration of an existing instance of this modifier.
	keys.ability:ApplyDataDrivenModifier(keys.caster, keys.target, "modifier_queen_of_pain_shadow_strike", {})
	
	keys.target:SetModifierStackCount("modifier_queen_of_pain_shadow_strike", keys.caster, shadow_strike_stack_count)
	
	if keys.target:IsHero() then  --Refund some mana if an enemy hero is hit.
		local queen_of_pain_shadow_strike_mana_regen_particle = ParticleManager:CreateParticle("particles/units/heroes/hero_queenofpain/queen_of_pain_mana_refund.vpcf", PATTACH_ABSORIGIN, keys.caster)
		keys.caster:GiveMana(keys.ManaRefund)
	end
	
	keys.target:EmitSound("Hero_QueenOfPain.ShadowStrike")
	
	--Deal base damage plus bonus damage per stack.
	ApplyDamage({victim = keys.target, attacker = keys.caster, damage = keys.BaseDamage + (shadow_strike_stack_count - 1) * keys.BonusDamagePerStack, damage_type = DAMAGE_TYPE_MAGICAL})
end


--[[ ============================================================================================================
	Hero name: Queen of Pain
	Ability name: Crypt Swarm
	Pre: Called regularly while Crypt Swarm's projectile exists.  Internally, the projectile is technically a unit,
		so keys.caster in this function refers to the projectile itself.
	Effect: Moves the projectile in its stored direction.  If it hits an enemy unit, it stuns and splits into two 
		projectiles moving in perpendicular directions.  If either of these two	projectiles	hits an enemy unit, they 
		ensnare and split into two more projectiles moving in perpendicular directions.  These final projectiles slow 
		if they hit an enemy unit and do not split again.  Projectiles get removed if they remain further than a
		certain distance away from the casting hero for a few seconds.
	Note: Normally I would refactor this function into smaller, more abstract functions where possible, but I have
		left it as one longer function in the interest of keeping it easier to follow for this example.
================================================================================================================= ]]
function death_prophet_crypt_swarm_projectile_on_interval_think(keys)
	--This if statement is included because it appears that modifiers with OnIntervalThink can call functions like this for a few frames after keys.caster:RemoveSelf() is called.
	if IsValidEntity(keys.caster) and keys.caster.crypt_swarm_direction ~= nil then
		local projectile_position = keys.caster:GetAbsOrigin()
		
		--Remove the projectile if it has moved off the edge of the map.
		if projectile_position.x < -8100 or projectile_position.x > 8100 or projectile_position.y < -8100 or projectile_position.y > 8100 then
			keys.caster:RemoveSelf()
		else
			--Set up the projectile's destination point for this frame, using the direction in which it is traveling.
			local next_point = (keys.caster.crypt_swarm_direction * keys.caster.crypt_swarm_speed) + projectile_position
			next_point = GetGroundPosition(next_point, keys.caster)
			next_point.z = next_point.z + 100  --Keep the projectile in the air, not on the ground.
			
			--Check to see if the projectile will collide with any enemy hero in its new position.
			local enemy_unit_collided_list = FindUnitsInRadius(keys.caster:GetTeam(), next_point, nil, keys.caster.crypt_swarm_width, DOTA_UNIT_TARGET_TEAM_ENEMY, 
											DOTA_UNIT_TARGET_HERO + DOTA_UNIT_TARGET_BASIC, DOTA_UNIT_TARGET_FLAG_NONE, FIND_ANY_ORDER, false)

			local closest_unit = nil
			local closest_unit_distance = nil
			
			--Tracks whether the projectile is still colliding with the unit that previously caused it to split into two (if any exist).
			--This variable is used to ensure that split projectiles do not recollide with the same unit until they first stop colliding with it.
			local still_colliding_with_split_unit = false
			
			--Iterate through all the units the projectile is colliding with at its new position, and select the one that's closest to the projectile's 
			--previous position, which we will assume it would have hit first.
			for i, individual_unit in ipairs(enemy_unit_collided_list) do  
				if individual_unit:IsAlive() then  --Ignore this found unit if it's dead.
					if keys.caster.crypt_swarm_unit_split_on ~= nil and individual_unit == keys.caster.crypt_swarm_unit_split_on then
						still_colliding_with_split_unit = true
					elseif closest_unit == nil then  --If this is the first unit we've come across in this loop.
						closest_unit = individual_unit
						closest_unit_distance = individual_unit:GetRangeToUnit(keys.caster)
					else
						local range_to_unit = individual_unit:GetRangeToUnit(keys.caster)
						if range_to_unit < closest_unit_distance then  --If this unit's location is the closest we've seen to the projectile's previous position.
							closest_unit = individual_unit
							closest_unit_distance = range_to_unit
						end
					end
				end
			end
			
			--As soon as the projectile is no longer colliding with the unit it last split on, allow that unit to be able to be hit again.
			if keys.caster.crypt_swarm_unit_split_on ~= nil and still_colliding_with_split_unit == false then
				keys.caster.crypt_swarm_unit_split_on = nil
			end
			
			--Officially move the projectile to its new position for the current frame.
			keys.caster:SetAbsOrigin(next_point)
			
			if closest_unit ~= nil and IsValidEntity(keys.caster.parent_death_prophet) then  --If we've hit an enemy hero with the projectile.
				local crypt_swarm_ability = keys.caster.parent_death_prophet:FindAbilityByName("death_prophet_crypt_swarm")
				
				if keys.caster.crypt_swarm_times_already_split == 0 then  --If this projectile has not split before and should therefore stun.
					--Create an explosion particle on the hit unit.
					local crypt_swarm_explosion_particle = ParticleManager:CreateParticle("particles/units/heroes/hero_death_prophet/death_prophet_crypt_swarm_explosion.vpcf", PATTACH_ABSORIGIN_FOLLOW, closest_unit)
					ParticleManager:SetParticleControlEnt(crypt_swarm_explosion_particle, 0, closest_unit, PATTACH_ABSORIGIN_FOLLOW, "follow_origin", closest_unit:GetAbsOrigin(), false)
					ParticleManager:SetParticleControl(crypt_swarm_explosion_particle, 1, Vector(245, 125, 255))
					
					closest_unit:EmitSound("Hero_DeathProphet.CarrionSwarm.Damage")
				
					local projectile_damage = crypt_swarm_ability:GetLevelSpecialValueFor("projectile_damage_after_" .. keys.caster.crypt_swarm_times_already_split .. "_splits", keys.caster.crypt_swarm_level - 1)
					ApplyDamage({victim = closest_unit, attacker = keys.caster.parent_death_prophet, damage = projectile_damage, damage_type = DAMAGE_TYPE_MAGICAL})
					
					--Apply a different debuff depending on how many times the projectile has previously split.
					if keys.caster.crypt_swarm_times_already_split == 0 then
						crypt_swarm_ability:ApplyDataDrivenModifier(keys.caster.parent_death_prophet, closest_unit, "modifier_death_prophet_crypt_swarm_stun", {})  --Stun
					elseif keys.caster.crypt_swarm_times_already_split == 1 then
						crypt_swarm_ability:ApplyDataDrivenModifier(keys.caster.parent_death_prophet, closest_unit, "modifier_death_prophet_crypt_swarm_ensnare", {})  --Ensnare
					elseif keys.caster.crypt_swarm_times_already_split >= 2 then
						crypt_swarm_ability:ApplyDataDrivenModifier(keys.caster.parent_death_prophet, closest_unit, "modifier_death_prophet_crypt_swarm_slow", {})  --Slow
					end
					
					--If the projectile has not already split twice, split it since we just hit a unit.
					if keys.caster.crypt_swarm_times_already_split < 2 then
						--Find the two perpendicular directions in which the new projectiles will travel.
						new_direction_1 = Vector(keys.caster.crypt_swarm_direction.y * -1, keys.caster.crypt_swarm_direction.x, keys.caster.crypt_swarm_direction.z):Normalized()
						new_direction_2 = Vector(keys.caster.crypt_swarm_direction.y, keys.caster.crypt_swarm_direction.x * -1, keys.caster.crypt_swarm_direction.z):Normalized()
						
						--Create the two new projectiles and pass on the current projectile's properties, which were initialized along with the first projectile.
						local loop_direction = new_direction_1
						for i=0, 1, 1 do
							local projectile_origin = closest_unit:GetAbsOrigin()
							projectile_origin = Vector(projectile_origin.x, projectile_origin.y, projectile_origin.z + 100)
							
							local new_crypt_swarm_projectile = CreateUnitByName("npc_dota_death_prophet_crypt_swarm", projectile_origin, false, nil, nil, keys.caster:GetTeam())
							new_crypt_swarm_projectile:SetAbsOrigin(projectile_origin)
							
							new_crypt_swarm_projectile.parent_death_prophet = keys.caster.parent_death_prophet 
							new_crypt_swarm_projectile.crypt_swarm_times_already_split = new_crypt_swarm_projectile.crypt_swarm_times_already_split + 1
							new_crypt_swarm_projectile.crypt_swarm_unit_split_on = closest_unit
							new_crypt_swarm_projectile.crypt_swarm_level = keys.caster.crypt_swarm_level
							new_crypt_swarm_projectile.crypt_swarm_direction = Vector(loop_direction.x, loop_direction.y, loop_direction.z)
							new_crypt_swarm_projectile.crypt_swarm_speed = crypt_swarm_ability:GetLevelSpecialValueFor("projectile_speed_after_" .. keys.caster.crypt_swarm_times_already_split .. "_split", keys.caster.crypt_swarm_level - 1) * .03
							new_crypt_swarm_projectile.crypt_swarm_width = keys.caster.crypt_swarm_width
							new_crypt_swarm_projectile.crypt_swarm_cull_distance = keys.caster.crypt_swarm_cull_distance
							new_crypt_swarm_projectile.crypt_swarm_cull_distance_duration = keys.caster.crypt_swarm_cull_distance_duration
							new_crypt_swarm_projectile.crypt_swarm_gametime_left_radius = keys.caster.crypt_swarm_gametime_left_radius
							
							new_crypt_swarm_projectile:SetForwardVector(new_crypt_swarm_projectile.crypt_swarm_direction)
							new_crypt_swarm_projectile:SetModelScale(1.5)
							
							--Attach the projectile particles.
							local crypt_swarm_projectile_particle = ParticleManager:CreateParticle("particles/units/heroes/hero_death_prophet/death_prophet_crypt_swarm_core_trail.vpcf", PATTACH_ABSORIGIN_FOLLOW, new_crypt_swarm_projectile)
							ParticleManager:SetParticleControlEnt(crypt_swarm_projectile_particle, 0, new_crypt_swarm_projectile, PATTACH_ABSORIGIN_FOLLOW, "follow_origin", projectile_origin, false)
							ParticleManager:SetParticleControl(crypt_swarm_projectile_particle, 1, Vector(113, 58, 118))
							
							loop_direction = new_direction_2
						end
					end
					
					keys.caster:RemoveSelf()  --Remove this projectile since it has collided with something already.
				end
			elseif IsValidEntity(keys.caster.parent_death_prophet) then  --If we have not hit an enemy with the projectile, check to see if the projectile should be removed due to distance from the casting hero.
				if keys.caster:GetRangeToUnit(keys.caster.parent_death_prophet) > keys.caster.crypt_swarm_cull_distance then
					if keys.caster.crypt_swarm_gametime_left_radius == nil then
						keys.caster.crypt_swarm_gametime_left_radius = GameRules:GetGameTime()
					elseif GameRules:GetGameTime() > (keys.caster.crypt_swarm_gametime_left_radius + keys.caster.crypt_swarm_cull_distance_duration) then
					--If the projectile has remained far away from the casting hero for a certain duration of time.
						keys.caster:RemoveSelf()
					end
				else
					keys.caster.crypt_swarm_gametime_left_radius = nil
				end
			end
		end
	end
end


--[[ ============================================================================================================
	Hero name: Chaos Knight
	Ability name: Shuffle
	Effect: Shuffles the positions of the inputted team's heroes.  If the caster has an Aghanim's Scepter item, 
		every hero is guaranteed to be moved to a new position in the shuffle.
		This function uses an implementation of a Fisher–Yates shuffle (also known as a Knuth shuffle).
	Additional parameters: keys.team_to_shuffle
================================================================================================================= ]]
function chaos_knight_shuffle_on_spell_start(keys)
	local shuffle_heroes = {}
	local shuffle_positions = {}
	local array_length = 0
	
	--Fill the table of heroes that should be shuffled.
	local herolist = HeroList:GetAllHeroes()	
	for i, individual_hero in ipairs(herolist) do
		if IsValidEntity(individual_hero) and individual_hero:IsAlive() and individual_hero:GetTeam() == keys.team_to_shuffle then
			--Play a particle effect and sound around every living hero.
			local shuffle_particle = ParticleManager:CreateParticle("particles/units/heroes/hero_chaos_knight/chaos_knight_phantasm.vpcf", PATTACH_ABSORIGIN_FOLLOW, individual_hero)
			individual_hero:EmitSound("Hero_ChaosKnight.Phantasm")
			
			--Store the heroes to be shuffled.
			shuffle_heroes[array_length] = individual_hero
			shuffle_positions[array_length] = individual_hero:GetAbsOrigin()
			array_length = array_length + 1
		end
	end

	--Use a Knuth shuffle to shuffle the positions of the heroes.
	local shuffle_index = 0
	while shuffle_heroes[shuffle_index] ~= nil and shuffle_heroes[shuffle_index + 1] ~= nil do
		local index_to_swap_with = 0
		if keys.caster:HasScepter() then --If the caster has an Aghanim's Scepter, ensure that each unit will be moved to a different position.
			index_to_swap_with = RandomInt(shuffle_index + 1, array_length - 1)
		else
			index_to_swap_with = RandomInt(shuffle_index, array_length - 1)
		end
		
		--Swap the stored positions.
		local shuffle_position_temp = Vector(shuffle_positions[shuffle_index].x, shuffle_positions[shuffle_index].y, shuffle_positions[shuffle_index].z)
		shuffle_positions[shuffle_index] = shuffle_positions[index_to_swap_with]
		shuffle_positions[index_to_swap_with] = shuffle_position_temp
		
		shuffle_index = shuffle_index + 1
	end
		
	--Move the heroes to their new positions.
	for i = 0, array_length - 1, 1 do
		if shuffle_heroes[i] ~= nil and shuffle_positions[i] ~= nil then
			FindClearSpaceForUnit(shuffle_heroes[i], shuffle_positions[i], false)
		end
	end
end


--[[ ============================================================================================================
	Hero name: Warlock
	Ability name: Firestorm
	Effect: Warlock calls a number of fireballs from the sky to successively and randomly land in the targeted area.
		Upon landing, a fireball will deal damage to all nearby enemy units, after which it will deal damage per second
		to nearby enemy units while it remains on the ground.  Some seconds after landing, it will explode, dealing 
		damage to nearby enemy units.
	Additional parameters: keys.NumFireballs, keys.FireballCastRadius, keys.FireballLandDelay, keys.FireballDelayBetweenSpawns,
		keys.FireballVisionRadius, keys.FireballDamageAoE, keys.FireballLandingDamage, keys.FireballDuration,
		keys.FireballExplosionDamage
	Note: Normally I would refactor this function into smaller, more abstract functions where possible, but I have
		left it as one longer function in the interest of keeping it easier to follow for this example.
================================================================================================================= ]]
function warlock_firestorm_on_spell_start(keys)
	local caster_point = keys.caster:GetAbsOrigin()
	local target_point = keys.target_points[1]
	
	local caster_point_ground = Vector(caster_point.x, caster_point.y, 0)
	local target_point_ground = Vector(target_point.x, target_point.y, 0)
	
	local point_difference_normalized = (target_point_ground - caster_point_ground):Normalized()
	
	keys.caster:EmitSound("Hero_EarthSpirit.Petrify")
	keys.caster:EmitSound("Hero_Warlock.RainOfChaos")
	
	--Create an invisible dummy unit at the center of the targeted area in order to emit a sound.
	--The requirement of such a unit in order to play a sound at a point is a weakness of the Dota 2 modding tools.
	local fireball_sound_unit = CreateUnitByName("npc_dota_warlock_firestorm_fireball_explosion_unit", target_point, false, nil, nil, keys.caster:GetTeam())
	local dummy_unit_ability = fireball_sound_unit:FindAbilityByName("dummy_unit_passive")  --This ability ensures the unit will ignore collision and has properties like invisibility and untargetablility.
	if dummy_unit_ability ~= nil then
		dummy_unit_ability:SetLevel(1)
	end
	fireball_sound_unit:EmitSound("Hero_EarthSpirit.Magnetize.End")
	
	--Create a timer that will clean up this dummy unit once we no longer want the sound playing.
	Timers:CreateTimer({
		endTime = keys.FireballDuration + (keys.FireballDelayBetweenSpawns * keys.NumFireballs) + keys.FireballLandDelay + 3,
		callback = function()
			fireball_sound_unit:RemoveSelf()
		end
	})
	
	--Spawn the fireballs, with a slight delay between each.
	local fireballs_spawned_so_far = 0
	Timers:CreateTimer({
		callback = function()
			--Select a random point within the radius around the target point.
			local random_x_offset = RandomInt(0, keys.FireballCastRadius) - (keys.FireballCastRadius / 2)
			local random_y_offset = RandomInt(0, keys.FireballCastRadius) - (keys.FireballCastRadius / 2)
			local fireball_landing_point = Vector(target_point.x + random_x_offset, target_point.y + random_y_offset, target_point.z)
			fireball_landing_point = GetGroundPosition(fireball_landing_point, nil)
			
			--Create a particle effect consisting of the fireball falling from the sky and landing at the target point.
			--Particles are created with Source 2's particle editor and control points can be set as parameters.
			local fireball_spawn_point = (fireball_landing_point - (point_difference_normalized * 300)) + Vector (0, 0, 800)
			local fireball_fly_particle_effect = ParticleManager:CreateParticle("particles/units/heroes/hero_warlock/warlock_firestorm_fireball_fly.vpcf", PATTACH_ABSORIGIN, keys.caster)
			ParticleManager:SetParticleControl(fireball_fly_particle_effect, 0, fireball_spawn_point)
			ParticleManager:SetParticleControl(fireball_fly_particle_effect, 1, fireball_landing_point)
			ParticleManager:SetParticleControl(fireball_fly_particle_effect, 2, Vector(keys.FireballLandDelay, 0, 0))
			
			--Spawn the landed fireball when the particle effect will have visually appeared to land.
			Timers:CreateTimer({
				endTime = keys.FireballLandDelay,  --When this timer will first execute.
				callback = function()
					local fireball_unit = nil
					
					--Creating and destroying a unit consumes a nontrivial amount of resources, and I found that creating new ones in rapid succession (one for each fireball)
					--was causing issues with lag, particularly when multiple players were casting this spell simultaneously.  Stationary units use almost no resources, so I
					--devised a system where a large amount of fireball units are created offscreen when the game begins.  These fireball units are "checked out" and moved to
					--their landing locations as needed, and will be "checked back in" and moved back offscreen when we are done with them.
					
					--Initialize 32 ready-to-use fireball units that can be checked out (enough for two concurrent instances of the spell), if this has not already been done on startup.
					if warlock_firestorm_fireballs == nil then
						for i=0, 31, 1 do
							local fireball_unit = CreateUnitByName("npc_dota_warlock_firestorm_rook_unit", Vector(7000, 7000, 128), false, nil, nil, DOTA_TEAM_GOODGUYS)
							local fireball_unit_ability = fireball_unit:FindAbilityByName("warlock_firestorm_rook_fireball")
							if fireball_unit_ability ~= nil then
								fireball_unit_ability:SetLevel(1)
							end
							fireball_unit_ability:ApplyDataDrivenModifier(fireball_unit, fireball_unit, "dummy_modifier_no_health_bar", nil)
							
							warlock_firestorm_fireballs[i] = fireball_unit
						end
					end
					
					--Check out a waiting fireball unit (so we don't have to create a new one and cause lag).
					local i = 0
					while i <= 31 and fireball_unit == nil do
						if warlock_firestorm_fireballs[i] ~= nil then
							fireball_unit = warlock_firestorm_fireballs[i]
							warlock_firestorm_fireballs[i] = nil
						end
						i = i + 1
					end
					
					if fireball_unit == nil then  --If all 32 fireball units are currently in use, create a new temporary one regardless of resource usage.
						local fireball_unit = CreateUnitByName("npc_dota_warlock_firestorm_rook_unit", Vector(7000, 7000, 128), false, nil, nil, DOTA_TEAM_GOODGUYS)
						local fireball_unit_ability = fireball_unit:FindAbilityByName("warlock_firestorm_rook_fireball")
						if fireball_unit_ability ~= nil then
							fireball_unit_ability:SetLevel(1)
						end
						fireball_unit_ability:ApplyDataDrivenModifier(fireball_unit, fireball_unit, "dummy_modifier_no_health_bar", nil)
					end
					
					--Move the fireball to its landing location.
					fireball_unit:SetTeam(keys.caster:GetTeam())
					fireball_unit:SetAbsOrigin(fireball_landing_point)
					fireball_unit:SetHealth(fireball_unit:GetMaxHealth())

					fireball_unit:RemoveModifierByName("dummy_modifier_no_health_bar")
					fireball_unit.firestorm_fireball_time_to_explode = GameRules:GetGameTime() + keys.FireballDuration

					local fireball_ground_particle_effect = ParticleManager:CreateParticle("particles/units/heroes/hero_warlock/warlock_firestorm_fireball.vpcf", PATTACH_ABSORIGIN, fireball_unit)
			
					fireball_unit:SetDayTimeVisionRange(keys.FireballVisionRadius)
					fireball_unit:SetNightTimeVisionRange(keys.FireballVisionRadius)
					
					fireball_sound_unit:StopSound("Hero_EarthSpirit.RollingBoulder.Target")
					fireball_sound_unit:StopSound("Hero_Phoenix.FireSpirits.Cast")
					fireball_sound_unit:EmitSound("Hero_EarthSpirit.RollingBoulder.Target")
					fireball_sound_unit:EmitSound("Hero_Phoenix.FireSpirits.Cast")
					
					--Make the fireball deal damage over time in a radius.
					local firestorm_ability = fireball_unit:FindAbilityByName("warlock_firestorm")
					firestorm_ability:ApplyDataDrivenModifier(keys.caster, fireball_unit, "modifier_warlock_firestorm_fireball_duration", nil)
					firestorm_ability:ApplyDataDrivenModifier(keys.caster, fireball_unit, "modifier_warlock_firestorm_fireball_damage_over_time_aura_emitter", nil)					
					
					--Damage nearby enemy units with one-time fireball landing damage.
					local nearby_enemy_units = FindUnitsInRadius(keys.caster:GetTeam(), fireball_landing_point, nil, keys.FireballDamageAoE, DOTA_UNIT_TARGET_TEAM_ENEMY,
						DOTA_UNIT_TARGET_HERO + DOTA_UNIT_TARGET_BASIC, DOTA_UNIT_TARGET_FLAG_NONE, FIND_ANY_ORDER, false)
					for i, individual_unit in ipairs(nearby_enemy_units) do
						ApplyDamage({victim = individual_unit, attacker = keys.caster, damage = keys.FireballLandingDamage, damage_type = DAMAGE_TYPE_MAGICAL})
					end
					
					--Explode the fireball when it is supposed to expire.
					Timers:CreateTimer({
						endTime = keys.FireballDuration,
						callback = function()
							ParticleManager:DestroyParticle(fireball_ground_particle_effect, false)
							
							local fireball_explosion_particle_effect = ParticleManager:CreateParticle("particles/units/heroes/hero_warlock/warlock_firestorm_fireball_explosion.vpcf", PATTACH_ABSORIGIN, fireball_unit)
							fireball_sound_unit:EmitSound("Hero_EarthSpirit.RollingBoulder.Destroy")
							
							--Damage nearby enemy units with fireball explosion damage.
							local nearby_enemy_units = FindUnitsInRadius(keys.caster:GetTeam(), fireball_landing_point, nil, keys.FireballDamageAoE, DOTA_UNIT_TARGET_TEAM_ENEMY,
								DOTA_UNIT_TARGET_HERO + DOTA_UNIT_TARGET_BASIC, DOTA_UNIT_TARGET_FLAG_NONE, FIND_ANY_ORDER, false)
							for i, individual_unit in ipairs(nearby_enemy_units) do
								ApplyDamage({victim = individual_unit, attacker = keys.caster, damage = keys.FireballExplosionDamage, damage_type = DAMAGE_TYPE_MAGICAL})
							end
							
							fireball_unit:RemoveModifierByName("modifier_warlock_firestorm_fireball_duration")
							fireball_unit:RemoveModifierByName("modifier_warlock_firestorm_fireball_damage_over_time_aura_emitter")
							
							local firestorm_fireball_ability = fireball_unit:FindAbilityByName("warlock_firestorm_fireball")
							firestorm_fireball_ability:ApplyDataDrivenModifier(fireball_unit, fireball_unit, "dummy_modifier_no_health_bar", {Duration = -1})
							
							fireball_unit:SetAbsOrigin(Vector(7000, 7000, 128))  --Move the fireball back off the map.
					
							--Check the fireball unit back in.
							local i = 0
							while i <= 31 and fireball_unit ~= nil do
								if warlock_firestorm_fireballs[i] == nil then
									warlock_firestorm_fireballs[i] = fireball_unit
									fireball_unit = nil
								end
								i = i + 1
							end
						end
					})
				end
			})
			
			fireballs_spawned_so_far = fireballs_spawned_so_far + 1
			if fireballs_spawned_so_far >= keys.NumFireballs then --If this was the last fireball we needed to spawn, don't call the function again.
				return  
			else  --Spawn another fireball shortly.
				return keys.FireballDelayBetweenSpawns
			end
		end
	})
end