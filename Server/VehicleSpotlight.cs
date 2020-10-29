//  VehicleSpotlight.cs
//  Author: Timothy Dexter
//  Release: 0.0.1
//  Date: 04/05/2019
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
//  Revision 0.0.1 2019/04/07 7:29 AM EDT TimothyDexter 
//  - Initial release
//   

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using Roleplay.SharedClasses;

namespace Roleplay.Server.Classes.Jobs.Police
{
	internal static class VehicleSpotlight
	{
		private const int LightTimeoutMin = 30;

		private static int _spotlightId;
		private static bool _spotlightBeingAdded;

		public static readonly Dictionary<int, DateTime> ActiveSpotlights =
			new Dictionary<int, DateTime>();

		private static bool _activeSpotlightLock;

		public static void Init() {
			Server.ActiveInstance.RegisterEventHandler( "VehicleSpotlight.AddSpotlight",
				new Action<Player>( HandleAddSpotlight ) );

			Server.ActiveInstance.RegisterEventHandler( "VehicleSpotlight.RemoveSpotlight",
				new Action<Player, int>( HandleRemoveSpotlight ) );

			Server.ActiveInstance.RegisterEventHandler( "VehicleSpotlight.GetActiveSpotlights",
				new Action<Player>( HandleGetActiveSpotlights ) );

			Task.Factory.StartNew( async () => { await PeriodicSpotlightCleanupTask(); } );
		}

		/// <summary>
		///     Handles the get active spotlights.
		/// </summary>
		/// <param name="source">The source.</param>
		private static void HandleGetActiveSpotlights( [FromSource] Player source ) {
			bool activeLights = ActiveSpotlights.Any();
			source.TriggerEvent( "VehicleSpotlight.ToggleActiveSpotlights", activeLights );
		}

		/// <summary>
		///     Removes any stale spotlights
		/// </summary>
		private static async Task PeriodicSpotlightCleanupTask() {
			try {
				var spotlightsToRemove = new List<int>();
				var currTime = DateTime.Now;

				var timeout = DateTime.Now.AddMinutes( 2 );
				while( _activeSpotlightLock && DateTime.Now.CompareTo( timeout ) < 0 ) await BaseScript.Delay( 225 );

				if( !_activeSpotlightLock ) {
					_activeSpotlightLock = true;
					foreach( var spotlight in ActiveSpotlights ) {
						if( currTime.CompareTo( spotlight.Value.AddMinutes( LightTimeoutMin ) ) < 0 ) continue;
						spotlightsToRemove.Add( spotlight.Key );
					}

					_activeSpotlightLock = false;

					foreach( int lightToRemove in spotlightsToRemove ) RemoveSpotlight( lightToRemove );
				}

				await BaseScript.Delay( 60000 );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handles the add spotlight.
		/// </summary>
		/// <param name="source">The source.</param>
		private static async void HandleAddSpotlight( [FromSource] Player source ) {
			try {
				if( !SessionManager.SessionList.ContainsKey( source.Handle ) ) return;

				var timeout = DateTime.Now.AddMinutes( 5 );
				while( DateTime.Now.CompareTo( timeout ) < 0 && _spotlightBeingAdded ) await BaseScript.Delay( 275 );

				if( _spotlightBeingAdded ) return;

				_spotlightBeingAdded = true;

				Log.Verbose( $"Received spotlight id = {_spotlightId}" );

				source.TriggerEvent( "VehicleSpotlight.AddSpotlight", _spotlightId );
				ActiveSpotlights.Add( _spotlightId, DateTime.Now );
				_spotlightId = _spotlightId + 1;

				_spotlightBeingAdded = false;

				SessionManager.SessionList.Where( p => p.Value.IsPlaying ).ToList().ForEach( p =>
					p.Value.TriggerEvent( "VehicleSpotlight.ToggleActiveSpotlights", true ) );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handles the remove spotlight.
		/// </summary>
		/// <param name="source">The source.</param>
		/// <param name="spotlightId">The spotlight identifier.</param>
		private static void HandleRemoveSpotlight( [FromSource] Player source, int spotlightId ) {
			try {
				if( !SessionManager.SessionList.ContainsKey( source.Handle ) ||
				    !ActiveSpotlights.ContainsKey( spotlightId ) ) return;

				RemoveSpotlight( spotlightId );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Removes spotlight and triggers clients if none remaining.
		/// </summary>
		/// <param name="spotlightId">The spotlight identifier.</param>
		private static async void RemoveSpotlight( int spotlightId ) {
			if( !ActiveSpotlights.ContainsKey( spotlightId ) ) return;

			var timeout = DateTime.Now.AddMinutes( 5 );
			while( _activeSpotlightLock && DateTime.Now.CompareTo( timeout ) < 0 ) await BaseScript.Delay( 225 );

			if( _activeSpotlightLock ) return;

			_activeSpotlightLock = true;
			ActiveSpotlights.Remove( spotlightId );
			_activeSpotlightLock = false;

			if( ActiveSpotlights.Any() ) return;

			SessionManager.SessionList.Where( p => p.Value.IsPlaying ).ToList().ForEach( p =>
				p.Value.TriggerEvent( "VehicleSpotlight.ToggleActiveSpotlights", false ) );
		}
	}
}