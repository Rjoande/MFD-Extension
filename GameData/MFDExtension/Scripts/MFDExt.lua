-- Shared navigation logic for the MFD Extended additive branch.
--
-- A-G, R1-R7 and STBY on MAS_JSI_BasicMFD are NOT per-page softkeys: each is
-- wired once, prop-wide, straight to a fixed action via onClick. To let them
-- do double duty - native destination from host pages, ours from ours - the
-- decision has to live here, keyed on the page currently showing.
--
-- Every bay button shares the same three-way behavior through
-- MFDExt_Redirect: from a host page, whatever that button natively did; from
-- another page of ours, jump to this button's own bay; from a page of its
-- OWN bay, call an optional per-page override - see HOSTING.md for the
-- convention a hosted bay uses to claim its own button while active.

local MFDExt_OwnPages = {
	["MFDExt_Stby"] = true,
	["MFDExt_SA_Placeholder"] = true,
	-- RealBattery's own three-page cycle. ALL THREE must be listed, not just
	-- the entry page: without an entry a page doesn't count as one of ours,
	-- and a button press from it falls through to a host page instead of
	-- continuing the cycle.
	["MFDExt_BATT_EPS"] = true,
	["MFDExt_BATT"] = true,
	["MFDExt_BATT_Fleet"] = true,
	["MFDExt_KRAB_Placeholder"] = true,
	["MFDExt_KRILL_Placeholder"] = true,
	["MFDExt_ILS"] = true,
	["MFDExt_Unclaimed"] = true,
	["MFDExt_CAS"] = true,
	-- ELEC bay (R3), three pages of our own - every one listed, same rule
	-- as the BATT trio above.
	["MFDExt_ELEC"] = true,
	["MFDExt_ELEC_Sources"] = true,
	["MFDExt_ELEC_Loads"] = true,
	-- TCS bay (R4), three pages of our own - same rule.
	["MFDExt_TCS"] = true,
	["MFDExt_TCS_Loops"] = true,
	["MFDExt_TCS_Reactors"] = true,
}

-- A hosted bay may register a function here, keyed by page name, to override
-- what its button does while that exact page is already showing. The lazy
-- init is defensive and belongs in the hosting script too: MAS_LUA scripts
-- share one global environment on the prop, in unspecified order.
MFDExt_OwnButtonOverrides = MFDExt_OwnButtonOverrides or {}

-- `ownPages` (optional, 4th arg) is the set of every page belonging to this
-- bay, ownPage included, so a bay can chain through MORE than two of its own
-- pages on one button. Omitted, this reduces to `current == ownPage` and
-- behaves exactly as it did before the parameter existed.
--
-- When `current` belongs to this bay it is CURRENT's own override that runs,
-- not ownPage's, which is what lets each page of a bay decide where the next
-- press goes. A page with no override registered simply does nothing.
--
-- Cross-bay behavior is untouched: a button only ever passes its own
-- `ownPages`, so pressing A while sitting on a BATT page still takes the
-- plain "jump to bay A" branch and never consults B's override chain.
local function MFDExt_Redirect(monitorID, ownPage, hostFallback, ownPages)
	local current = fc.GetPersistent(monitorID)
	local mine = (ownPages and ownPages[current]) or (current == ownPage)
	if mine then
		local override = MFDExt_OwnButtonOverrides[current]
		if override then
			override(monitorID)
		end
	elseif MFDExt_OwnPages[current] then
		fc.SetPersistent(monitorID, ownPage)
	else
		hostFallback(monitorID)
	end
end

-- Button A ("SA"). Its host fallback replays the native softkey dispatch, so
-- every host page keeps whatever it defined for softkey 9 - the host's own
-- home page reads a per-monitor preference there, not a fixed target, and
-- hardcoding one would break it.
function MFDExt_ButtonA(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_SA_Placeholder", function(id)
		fc.SendSoftkey(id, 9)
	end)
end

-- THEMATIC LAYOUT, on the EICAS/ECAM model: top row = flight and command
-- (SA, ILS, FADEC, SWC), bottom row = vessel systems (CAS, BMS, ELEC, TCS).
-- Hosted bays are reached by PAGE NAME, so moving one to another button
-- costs the hosting repos nothing - only this file and the hub's label row
-- change. Every button keeps ITS OWN native host fallback whatever bay it
-- carries: the fallback belongs to the physical key, not to the bay.

-- Button B ("ILS") - hosts NavInstruments, rescued from its own dead
-- RPM-only patches (see Pages/MFDExt_ILS.cfg). Incidentally fixes an
-- upstream typo: the host's own onClick sends "MAS_JSI_BasicMFD_Graphs",
-- missing the "B_", which was never a registered page name - our host
-- branch supplies the correct target.
function MFDExt_ButtonB(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_ILS", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_B_Graphs")
	end)
end

-- Button C ("KRAB").
function MFDExt_ButtonC(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_KRAB_Placeholder", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_C_Targeting")
	end)
end

-- Button D ("KRILL"). Native softkey 12, same dispatch as A.
function MFDExt_ButtonD(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_KRILL_Placeholder", function(id)
		fc.SendSoftkey(id, 12)
	end)
end

-- Buttons E and F are free: both share the Unclaimed placeholder like G,
-- with their native targets preserved from a host page.
function MFDExt_ButtonE(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_E_VesselView")
	end)
end

function MFDExt_ButtonF(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_F_EngineIgnitor")
	end)
end

-- E, F, G and R5-R7 are not real bays: the six free ones share one
-- placeholder page instead of leaking into the host's own world when pressed
-- from inside ours (see Pages/MFDExt_Unclaimed.cfg). Native behavior outside
-- our world is untouched: all of them are fixed onClick except R2, which
-- routes through fc.SendSoftkey(17) - never assume one button's scheme from
-- another's, check each on the real source.

function MFDExt_ButtonG(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_G_DPAI")
	end)
end

-- R1 ("CAS") - our own textual fault-summary page, backed by
-- MFDExtCasModule.
function MFDExt_ButtonR1(monitorID) -- NAV
	MFDExt_Redirect(monitorID, "MFDExt_CAS", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_1_Landing")
	end)
end

-- R2 ("BMS") - RealBattery's Battery Management System, labelled by function
-- rather than by mod name like FADEC/SWC.
--
-- Three-page cycle: EPS summary (the entry page, where every jump from
-- outside the bay lands) -> per-vessel telemetry -> fleet view -> back. The
-- three fc.SetPersistent calls that drive the chain live in RealBattery's own
-- script, keyed by page name in MFDExt_OwnButtonOverrides; this function only
-- lists which pages belong to the bay. R2 is the one softkey-routed key of
-- the bottom row (17), replayed as is.
local MFDExt_BATT_Pages = {
	["MFDExt_BATT_EPS"] = true,
	["MFDExt_BATT"] = true,
	["MFDExt_BATT_Fleet"] = true,
}

function MFDExt_ButtonR2(monitorID) -- ORB
	MFDExt_Redirect(monitorID, "MFDExt_BATT_EPS", function(id)
		fc.SendSoftkey(id, 17)
	end, MFDExt_BATT_Pages)
end

-- R3 ("ELEC") - DynamicBatteryStorage's electrical ledger, backed by
-- MFDExtElecModule. Three pages of our own on one button, SUMMARY ->
-- SOURCES -> LOADS -> SUMMARY, on the same ownPages/override chain
-- RealBattery uses on R2 - except that the overrides live in THIS script,
-- since the bay is ours.
local MFDExt_ELEC_Pages = {
	["MFDExt_ELEC"] = true,
	["MFDExt_ELEC_Sources"] = true,
	["MFDExt_ELEC_Loads"] = true,
}

MFDExt_OwnButtonOverrides["MFDExt_ELEC"] = function(monitorID)
	fc.SetPersistent(monitorID, "MFDExt_ELEC_Sources")
end
MFDExt_OwnButtonOverrides["MFDExt_ELEC_Sources"] = function(monitorID)
	fc.SetPersistent(monitorID, "MFDExt_ELEC_Loads")
end
MFDExt_OwnButtonOverrides["MFDExt_ELEC_Loads"] = function(monitorID)
	fc.SetPersistent(monitorID, "MFDExt_ELEC")
end

function MFDExt_ButtonR3(monitorID) -- DOCK
	MFDExt_Redirect(monitorID, "MFDExt_ELEC", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_3_Docking")
	end, MFDExt_ELEC_Pages)
end

-- R4 ("TCS", Thermal Control System) - SystemHeat's heat loops and reactors,
-- backed by MFDExtTcsModule. Three pages of our own on one button, SUMMARY
-- -> LOOPS -> REACTORS -> SUMMARY, same chain as ELEC above.
local MFDExt_TCS_Pages = {
	["MFDExt_TCS"] = true,
	["MFDExt_TCS_Loops"] = true,
	["MFDExt_TCS_Reactors"] = true,
}

MFDExt_OwnButtonOverrides["MFDExt_TCS"] = function(monitorID)
	fc.SetPersistent(monitorID, "MFDExt_TCS_Loops")
end
MFDExt_OwnButtonOverrides["MFDExt_TCS_Loops"] = function(monitorID)
	fc.SetPersistent(monitorID, "MFDExt_TCS_Reactors")
end
MFDExt_OwnButtonOverrides["MFDExt_TCS_Reactors"] = function(monitorID)
	fc.SetPersistent(monitorID, "MFDExt_TCS")
end

function MFDExt_ButtonR4(monitorID) -- DATA
	MFDExt_Redirect(monitorID, "MFDExt_TCS", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_4_ShipInfo")
	end, MFDExt_TCS_Pages)
end

function MFDExt_ButtonR5(monitorID) -- CREW
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_5_CrewInfo")
	end)
end

function MFDExt_ButtonR6(monitorID) -- RSRC
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_6_Resources")
	end)
end

function MFDExt_ButtonR7(monitorID) -- EXT
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_7_Cameras")
	end)
end

-- STBY is also a fixed, prop-wide button (not a softkey) - unconditionally
-- "MAS_JSI_BasicMFD_Home" natively. From inside our world it needs to go up
-- ONE level instead: leaf page -> hub (MFDExt_Stby), hub -> host home.
function MFDExt_ButtonSTBY(monitorID)
	local current = fc.GetPersistent(monitorID)
	if current == "MFDExt_Stby" then
		fc.SetPersistent(monitorID, "MAS_JSI_BasicMFD_Home")
	elseif MFDExt_OwnPages[current] then
		fc.SetPersistent(monitorID, "MFDExt_Stby")
	else
		fc.SetPersistent(monitorID, "MAS_JSI_BasicMFD_Home")
	end
end
