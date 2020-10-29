//  VehicleSpotlight.cs
//  Author: Timothy Dexter
//  Release: 0.0.1
//  Date: 04/02/2019
//  
//   
//  Known Issues
//   
//   
//  Please send any edits/improvements/bugs to this script back to the author. 
//   
//  Usage 
//   
//   
//  History:
//  Revision 0.0.1 2019/04/06 11:12 PM EDT TimothyDexter 
//  - Initial release
//   

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using Roleplay.Client.Classes.Player;
using Roleplay.Client.Helpers;
using Roleplay.Enums;
using Roleplay.SharedClasses;

namespace Roleplay.Client.Classes.Jobs.Police
{
	internal static class VehicleSpotlight
	{
		private const int ClientShadowId = 1;
		private const float InitHeading = 90f;
		private const float SpotlightDirectionOffsetMagnitude = 70f;
		private const float MaxHeightDelta = 25f;
		private const float MaxHeadingDelta = 75f;
		private const float SpotlightOffValue = -999f;

		private static readonly Dictionary<int, Vector3> VehicleLightOriginOffsets = new Dictionary<int, Vector3> {
			{-1627000575, new Vector3( -0.800f, 0.900f, 0.500f )}, // police2
			{1624609239, new Vector3( -0.800f, 0.900f, 0.500f )}, // chgr
			{1341474454, new Vector3( -0.800f, 0.650f, 0.700f )}, // 2015polstang
			{515485475, new Vector3( -0.800f, 1.500f, 0.800f )}, // silv
			{1405586763, new Vector3( -0.900f, 1.000f, 0.500f )}, // 16exp
			{1825110413, new Vector3( -0.850f, 1.100f, 1.100f )}, // tahoerb
			{-1046437422, new Vector3( -0.800f, 0.900f, 0.500f )} // pd1
		};
 
		private static bool _isDebugVehOffset = false;

		private static bool _isActiveSpotlightsInitialized;
		private static bool _isLightOn;

		private static Vector3 _spotlightDirection = Vector3.Zero;
		private static float _lightHeading;
		private static bool _headingInit;
		private static float _lightHeight;
		private static bool _hasTransitionedToObserver;

		private static bool _activeLightsExist;
		private static readonly Dictionary<int, ObservedSpotlight> ActiveSpotlights =
			new Dictionary<int, ObservedSpotlight>();
		private static VehicleList _cachedNearbyVehicles;
		private static DateTime _lastCacheUpdate;
		private static int _shadowIdCount = 2;

		/// <summary>
		///     Initializes this instance.
		/// </summary>
		public static void Init() {
			try {
				Client.ActiveInstance.RegisterTickHandler( ClientSpotlightTick );
				Client.ActiveInstance.RegisterTickHandler( SpotlightObserverTick );

				Client.ActiveInstance.RegisterEventHandler( "VehicleSpotlight.AddSpotlight",
					new Action<int>( HandleAddSpotlight ) );

				Client.ActiveInstance.RegisterEventHandler( "VehicleSpotlight.ToggleActiveSpotlights",
					new Action<bool>( HandleToggleActiveSpotlights ) );

				API.DecorRegister( "VehSpot.Id", (int)DecorType.DECOR_TYPE_INT );
				API.DecorRegister( "VehSpot.Heading", (int)DecorType.DECOR_TYPE_FLOAT );
				API.DecorRegister( "VehSpot.Height", (int)DecorType.DECOR_TYPE_FLOAT );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handles the toggle active spotlights.
		/// </summary>
		/// <param name="activeSpotlightsExist">if set to <c>true</c> [active spotlights exist].</param>
		private static void HandleToggleActiveSpotlights( bool activeSpotlightsExist ) {
			_activeLightsExist = activeSpotlightsExist;
		}

		/// <summary>
		///     Handles the add spotlight.
		/// </summary>
		/// <param name="spotlightId">The spotlight identifier.</param>
		private static void HandleAddSpotlight( int spotlightId ) {
			if( Cache.CurrentVehicle == null ) return;
			Log.Info($"Setting vehSpotlightId={spotlightId}");
			API.DecorSetInt( Cache.CurrentVehicle.Handle, "VehSpot.Id", spotlightId );
		}

		/// <summary>
		///     Client spotlight tick.
		/// </summary>
		/// <returns></returns>
		private static async Task ClientSpotlightTick() {
			try {
				if( !Session.HasJoinedRP ) {
					await BaseScript.Delay( 2000 );
					return;
				}

				if( !_isActiveSpotlightsInitialized ) {
					BaseScript.TriggerServerEvent( "VehicleSpotlight.GetActiveSpotlights" );
					await BaseScript.Delay( 3000 );
					_isActiveSpotlightsInitialized = true;
				}

				if( !Cache.IsPlayerDriving ) {
					int sleepTime = 100;
					if( _isLightOn ) {
						if( !_hasTransitionedToObserver ) {
							var entity = Entity.FromHandle( Cache.LastVehicle.Handle );
							if( entity != null && entity.Exists() ) {
								//Client is getting out of car, perform transition so spotlight doesn't blink.
								await EaseSpotlightTransitionForClient( entity, _lightHeading, _lightHeight, ClientShadowId );
								_hasTransitionedToObserver = true;
							}
						}
						sleepTime = 0;
					}

					await BaseScript.Delay( sleepTime );
					return;
				}

				_hasTransitionedToObserver = false;

				var veh = Cache.CurrentVehicle;
				if( veh == null || !VehicleLightOriginOffsets.ContainsKey( veh.Model.Hash ) ) {
					await BaseScript.Delay( 500 );
					return;
				}

				int vehHandle = veh.Handle;
				if( ControlHelper.IsControlJustPressed( Control.VehicleFlySelectTargetLeft ) )
					ToggleSpotlight( vehHandle );

				if( !_isLightOn ) return;

				_spotlightDirection = veh.ForwardVector;

				if( !_headingInit ) {
					InitializeSpotlight( vehHandle, InitHeading, _spotlightDirection.Z + 5f );
					_headingInit = true;
				}

				GetLightHeightInput( vehHandle );
				GetLightHeadingInput( vehHandle, veh.Heading );

				//Add debug Vector3 here on new vehicles to determine their offset and set _isDebugVehOffset to true
				var lightOrigin = veh.GetOffsetPosition( VehicleLightOriginOffsets[veh.Model.Hash] );
				if( _isDebugVehOffset ) {
					DrawLightOriginDebugBox( lightOrigin );
				}
				_spotlightDirection = GetSpotlightDirectionPos( veh, _lightHeading, _lightHeight );
				
				API.DrawSpotLightWithShadow( lightOrigin.X, lightOrigin.Y, lightOrigin.Z, _spotlightDirection.X,
					_spotlightDirection.Y, _spotlightDirection.Z, 221, 221,
					221, SpotlightDirectionOffsetMagnitude, 50f, 4.3f, 25f, 28.6f, ClientShadowId );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     The spotlight observer tick.
		/// </summary>
		/// <returns></returns>
		private static async Task SpotlightObserverTick() {
			try {
				if( !Session.HasJoinedRP ) {
					await BaseScript.Delay( 2000 );
					return;
				}

				if( !_activeLightsExist ) {
					await BaseScript.Delay( 500 );
					return;
				}

				if( DateTime.Now.CompareTo( _lastCacheUpdate.AddMilliseconds( 750 ) ) >= 0 ) {
					_cachedNearbyVehicles = new VehicleList();
					_lastCacheUpdate = DateTime.Now;
				}

				foreach( int vehHandle in _cachedNearbyVehicles ) {
					var entity = Entity.FromHandle( vehHandle );

					bool vehMissingSpotlight = ActiveSpotlights.ContainsKey( vehHandle ) || entity == null ||
					                           !entity.Exists() ||
					                           !API.DecorExistOn( vehHandle, "VehSpot.Heading" ) ||
					                           Math.Abs(API.DecorGetFloat( vehHandle, "VehSpot.Heading" ) - SpotlightOffValue) <= 1 ||
											   !API.DecorExistOn( vehHandle, "VehSpot.Height" ) ||
					                           Math.Abs(API.DecorGetFloat( vehHandle, "VehSpot.Height" ) - SpotlightOffValue) <= 1 ||
											   entity.Position.DistanceToSquared2D( Cache.PlayerPos ) > 3000f;

					if( vehMissingSpotlight ) continue;

					ActiveSpotlights.Add( vehHandle, new ObservedSpotlight( _shadowIdCount ) );
					_shadowIdCount = _shadowIdCount + 1;
				}

				if( !ActiveSpotlights.Any() ) {
					await BaseScript.Delay( 250 );
					return;
				}

				var spotlightsToRemove = new List<int>();
				foreach( var kvp in ActiveSpotlights ) {
					int vehHandle = kvp.Key;
					var entity = Entity.FromHandle( vehHandle );

					if( entity == null || ShouldRemoveActiveSpotlight( entity ) ) {
						spotlightsToRemove.Add( vehHandle );
						continue;
					}

					if( IsClientSpotlightActiveInVeh( vehHandle ) ) {
						if( kvp.Value.IsLightInitialized )
							kvp.Value.ReadyTransitionToClientDriver();
						else
							continue;
					}

					bool hasStartedTransition = await kvp.Value.HasStartedTransitionToClientDriver( entity );
					if( hasStartedTransition ) {
						//Client is getting in driver seat of car, perform transition so spotlight doesn't blink.
						continue;
					}

					float lightHeading = API.DecorGetFloat( vehHandle, "VehSpot.Heading" );
					float lightHeight = API.DecorGetFloat( vehHandle, "VehSpot.Height" );

					if( Math.Abs( API.DecorGetFloat( vehHandle, "VehSpot.Heading" ) - SpotlightOffValue ) <= 1 ||
					    Math.Abs( API.DecorGetFloat( vehHandle, "VehSpot.Height" ) - SpotlightOffValue ) <= 1 ) {
						spotlightsToRemove.Add( vehHandle );
						continue;
					}

						if( kvp.Value.IsLightInitialized ) {
						kvp.Value.UpdateHeadingAndHeight( lightHeading, lightHeight );
					}
					else {
						kvp.Value.InitializeSpotlight( lightHeading, lightHeight );
						kvp.Value.IsLightInitialized = true;
					}

					var lightOrigin = entity.GetOffsetPosition( VehicleLightOriginOffsets[entity.Model.Hash] );
					var spotlightDirection =
						GetSpotlightDirectionPos( entity, kvp.Value.CurrentHeading, kvp.Value.CurrentHeight );

					API.DrawSpotLightWithShadow( lightOrigin.X, lightOrigin.Y, lightOrigin.Z, spotlightDirection.X,
						spotlightDirection.Y, spotlightDirection.Z, 221, 221,
						221, SpotlightDirectionOffsetMagnitude, 50f, 4.3f, 25f, 28.6f, kvp.Value.ShadowId );
				}

				foreach( int light in spotlightsToRemove ) ActiveSpotlights.Remove( light );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Eases the spotlight transition for client.
		/// </summary>
		/// <param name="entity">The entity.</param>
		/// <param name="currentHeading">The current heading.</param>
		/// <param name="currentHeight">Height of the current.</param>
		/// <param name="shadowId">The shadow identifier.</param>
		/// <returns></returns>
		private static async Task EaseSpotlightTransitionForClient( Entity entity, float currentHeading,
			float currentHeight, int shadowId ) {
			if( !VehicleLightOriginOffsets.ContainsKey( entity.Model.Hash ) ) return;
			float brightness = 50f;
			var currentLightOrigin = entity.GetOffsetPosition( VehicleLightOriginOffsets[entity.Model.Hash] );
			var currentDirection = GetSpotlightDirectionPos( entity, currentHeading, currentHeight );
			while(  brightness > 0 ) {
				brightness = brightness - 1f;

				API.DrawSpotLightWithShadow( currentLightOrigin.X, currentLightOrigin.Y, currentLightOrigin.Z,
					currentDirection.X, currentDirection.Y, currentDirection.Z, 221, 221,
					221, SpotlightDirectionOffsetMagnitude, brightness, 4.3f, 25f, 28.6f, shadowId );

				await BaseScript.Delay( 0 );
			}
		}

		/// <summary>
		///     Determines whether [is client spotlight active in veh] [the specified veh handle].
		/// </summary>
		/// <param name="vehHandle">The veh handle.</param>
		/// <returns>
		///     <c>true</c> if [is client spotlight active in veh] [the specified veh handle]; otherwise, <c>false</c>.
		/// </returns>
		private static bool IsClientSpotlightActiveInVeh( int vehHandle ) {
			return Cache.IsPlayerDriving && Cache.CurrentVehicle != null &&
			       Cache.CurrentVehicle.Handle == vehHandle;
		}

		/// <summary>
		///     Should client remove active observed spotlight.
		/// </summary>
		/// <param name="entity">The entity.</param>
		/// <returns></returns>
		private static bool ShouldRemoveActiveSpotlight( Entity entity ) {
			int vehHandle = entity.Handle;
			return !entity.Exists() ||
			       !VehicleLightOriginOffsets.ContainsKey( entity.Model.Hash ) ||
			       !API.DecorExistOn( vehHandle, "VehSpot.Heading" ) ||
			       !API.DecorExistOn( vehHandle, "VehSpot.Height" ) ||
			       entity.Position.DistanceToSquared2D( Cache.PlayerPos ) > 3000f;
		}

		/// <summary>
		///     Toggle client spotlight
		/// </summary>
		/// <param name="vehHandle">The veh handle.</param>
		private static void ToggleSpotlight( int vehHandle ) {
			_isLightOn = !_isLightOn;

			if( _isLightOn ) {
				BaseScript.TriggerServerEvent( "VehicleSpotlight.AddSpotlight" );
				_headingInit = false;
			}
			else {
				if( API.DecorExistOn( vehHandle, "VehSpot.Id" ) ) {
					int spotlightId = API.DecorGetInt( vehHandle, "VehSpot.Id" );
					BaseScript.TriggerServerEvent( "VehicleSpotlight.RemoveSpotlight", spotlightId );
				}

				API.DecorSetFloat( vehHandle, "VehSpot.Height", SpotlightOffValue );
				API.DecorSetFloat( vehHandle, "VehSpot.Heading", SpotlightOffValue );
			}
		}

		/// <summary>
		///     Initializes client spotlight.
		/// </summary>
		/// <param name="vehHandle">The veh handle.</param>
		/// <param name="initHeading">The initialize heading.</param>
		/// <param name="initHeight">Height of the initialize.</param>
		private static void InitializeSpotlight( int vehHandle, float initHeading, float initHeight ) {
			if( API.DecorExistOn( vehHandle, "VehSpot.Heading" ) ) {
				if( Math.Abs( API.DecorGetFloat( vehHandle, "VehSpot.Heading" ) - SpotlightOffValue ) > 1 ) {
					initHeading = API.DecorGetFloat( vehHandle, "VehSpot.Heading" );
				}
			}

			_lightHeading = initHeading;

			if( API.DecorExistOn( vehHandle, "VehSpot.Height" ) ) {
				if( Math.Abs( API.DecorGetFloat( vehHandle, "VehSpot.Height" ) - SpotlightOffValue ) > 1 ) {
					initHeight = API.DecorGetFloat( vehHandle, "VehSpot.Height" );
				}
			}

			_lightHeight = initHeight;

			API.DecorSetFloat( vehHandle, "VehSpot.Heading", _lightHeading );
			API.DecorSetFloat( vehHandle, "VehSpot.Height", _lightHeight );
		}

		/// <summary>
		///     Gets the light height input.
		/// </summary>
		/// <param name="vehHandle">The veh handle.</param>
		private static void GetLightHeightInput( int vehHandle ) {
			float tempHeight = 0f;
			if( ControlHelper.IsControlPressed( Control.VehicleSubPitchUpOnly ) )
				tempHeight = _lightHeight + 1f;
			else if( ControlHelper.IsControlPressed( Control.VehicleSubPitchUpDown ) ) tempHeight = _lightHeight - 1f;

			if( Math.Abs( tempHeight ) > 0 )
				if( IsHeightValid( tempHeight ) ) {
					_lightHeight = tempHeight;
					API.DecorSetFloat( vehHandle, "VehSpot.Height", _lightHeight );
				}
		}

		/// <summary>
		///     Determines whether [is height valid] [the specified light height].
		/// </summary>
		/// <param name="lightHeight">Height of the light.</param>
		/// <returns>
		///     <c>true</c> if [is height valid] [the specified light height]; otherwise, <c>false</c>.
		/// </returns>
		private static bool IsHeightValid( float lightHeight ) {
			return lightHeight >= -MaxHeightDelta && lightHeight <= MaxHeightDelta;
		}

		/// <summary>
		///     Gets the light heading input.
		/// </summary>
		/// <param name="vehHandle">The veh handle.</param>
		/// <param name="vehicleHeading">The vehicle heading.</param>
		private static void GetLightHeadingInput( int vehHandle, float vehicleHeading ) {
			float tempHeading = 0f;
			if( ControlHelper.IsControlPressed( Control.VehicleSubTurnLeftOnly ) )
				tempHeading = (_lightHeading + 1f) % 360;
			else if( ControlHelper.IsControlPressed( Control.VehicleSubTurnRightOnly ) )
				tempHeading = (_lightHeading - 1f) % 360;

			if( Math.Abs( tempHeading ) > 0 )
				if( IsHeadingValid( vehicleHeading, vehicleHeading + tempHeading - InitHeading ) ) {
					_lightHeading = tempHeading;
					API.DecorSetFloat( vehHandle, "VehSpot.Heading", _lightHeading );
				}
		}

		/// <summary>
		///     Determines whether [is heading valid] [the specified veh heading].
		/// </summary>
		/// <param name="vehHeading">The veh heading.</param>
		/// <param name="lightHeading">The light heading.</param>
		/// <returns>
		///     <c>true</c> if [is heading valid] [the specified veh heading]; otherwise, <c>false</c>.
		/// </returns>
		private static bool IsHeadingValid( float vehHeading, float lightHeading ) {
			float diff = GetAbsoluteDegDiff( vehHeading, lightHeading );

			return diff <= MaxHeadingDelta;
		}

		/// <summary>
		///     Gets the absolute deg difference.
		/// </summary>
		/// <param name="startHeading">The start heading.</param>
		/// <param name="finishHeading">The finish heading.</param>
		/// <returns></returns>
		private static float GetAbsoluteDegDiff( float startHeading, float finishHeading ) {
			float normalized = finishHeading - startHeading;
			normalized = Math.Abs( (normalized + 180) % 360 - 180 );

			return normalized;
		}

		/// <summary>
		///     Gets the spotlight direction position.
		/// </summary>
		/// <param name="entity">The entity.</param>
		/// <param name="lightHeading">The light heading.</param>
		/// <param name="lightHeight">Height of the light.</param>
		/// <returns></returns>
		private static Vector3 GetSpotlightDirectionPos( Entity entity, float lightHeading, float lightHeight ) {
			float newHeading = (entity.Heading + lightHeading) % 360;

			double cosx = Math.Cos( newHeading * (Math.PI / 180f) );
			double siny = Math.Sin( newHeading * (Math.PI / 180f) );

			float deltaX = (float)(SpotlightDirectionOffsetMagnitude * cosx);
			float deltaY = (float)(SpotlightDirectionOffsetMagnitude * siny);

			return new Vector3( entity.ForwardVector.X + deltaX, entity.ForwardVector.Y + deltaY, lightHeight );
		}

		/// <summary>
		///     Draws the light origin debug box.
		/// </summary>
		/// <param name="lightOrigin">The light origin.</param>
		private static void DrawLightOriginDebugBox( Vector3 lightOrigin ) {
			API.DrawBox( lightOrigin.X, lightOrigin.Y, lightOrigin.Z, lightOrigin.X + 0.1f, lightOrigin.Y + 0.1f,
				lightOrigin.Z + 0.1f, 255, 105, 180, 255 );
		}

		/// <summary>
		///     Observer spotlight class
		/// </summary>
		private class ObservedSpotlight
		{
			public ObservedSpotlight( int shadowId ) {
				ShadowId = shadowId;
			}

			public float CurrentHeading { get; set; }
			public float CurrentHeight { get; set; }

			public bool IsLightInitialized { get; set; }

			private DateTime ClientEnteredDriverSeatTime { get; set; }

			public int ShadowId { get; }

			public void UpdateHeadingAndHeight( float lightHeading, float lightHeight ) {
				if( GetAbsoluteDegDiff( CurrentHeading, lightHeading ) > 1f )
					CurrentHeading = lightHeading < CurrentHeading
						? CurrentHeading - 1
						: CurrentHeading + 1;
				if( GetAbsoluteDegDiff( CurrentHeight, lightHeight ) > 1f )
					CurrentHeight = lightHeight < CurrentHeight
						? CurrentHeight - 1
						: CurrentHeight + 1;
			}

			public void InitializeSpotlight( float lightHeading, float lightHeight ) {
				CurrentHeading = lightHeading;
				CurrentHeight = lightHeight;
			}

			public void ReadyTransitionToClientDriver() {
				if( ClientEnteredDriverSeatTime == DateTime.MinValue ) ClientEnteredDriverSeatTime = DateTime.Now;
			}

			public async Task<bool> HasStartedTransitionToClientDriver( Entity entity ) {
				if( VehicleLightOriginOffsets.ContainsKey(entity.Model.Hash) && ClientEnteredDriverSeatTime != DateTime.MinValue &&
				    DateTime.Now.CompareTo( ClientEnteredDriverSeatTime.AddMilliseconds( 100 ) ) >= 0 ) {
					await EaseSpotlightTransitionForClient( entity, CurrentHeading, CurrentHeight, ShadowId );
					IsLightInitialized = false;
					ClientEnteredDriverSeatTime = DateTime.MinValue;
					return true;
				}

				return false;
			}
		}
	}
}