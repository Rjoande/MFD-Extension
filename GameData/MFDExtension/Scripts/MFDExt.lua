-- Shared navigation logic for the MFD Extended additive branch.
--
-- A-E and STBY on MAS_JSI_BasicMFD are NOT per-page softkeys - they're
-- wired once, prop-wide, straight to a fixed action via onClick (verified
-- on the real source, 2026-08-09/14/19). To let them do double duty
-- (native destination from host pages, ours from ours) the decision has to
-- live here, checking which page is currently showing.
--
-- Every bay button (A-E) shares the same three-way behavior via
-- MFDExt_Redirect: from a host page, do whatever that button natively did;
-- from any other page of ours, jump straight to this button's own bay;
-- from THIS button's own bay page already, call an optional per-page
-- override hook instead of doing nothing - see MFDExt_OwnButtonOverrides
-- below and HOSTING.md for the convention a hosted bay can use to claim
-- its own button (e.g. cycling its own internal sub-pages) while active.

local MFDExt_OwnPages = {
	["MFDExt_Stby"] = true,
	["MFDExt_SA_Placeholder"] = true,
	-- RealBattery's own three-page cycle (EPS -> per-vessel telemetry ->
	-- fleet view -> EPS, see MFDExt_ButtonB below and HOSTING.md's
	-- "Overriding your own button"). All three must be listed here, not
	-- just the entry page: without an entry, that page doesn't count as
	-- "already one of our own" and a button press from it falls through to
	-- a host page instead of continuing/closing the cycle - bug found and
	-- fixed for the 2-page version 2026-08-30, same rule extended here.
	["MFDExt_BATT_EPS"] = true,
	["MFDExt_BATT"] = true,
	["MFDExt_BATT_Fleet"] = true,
	["MFDExt_KRAB_Placeholder"] = true,
	["MFDExt_KRILL_Placeholder"] = true,
	["MFDExt_ILS"] = true,
	["MFDExt_Unclaimed"] = true,
	["MFDExt_CAS"] = true,
}

-- Hosted bays may register a function here, keyed by a page name, to
-- override what a button does while that exact page is already active
-- (default: nothing happens). Defensive lazy-init here AND in any hosting
-- mod's own script, since MAS_LUA scripts on the same prop all run into one
-- shared global environment but in unspecified order - see HOSTING.md.
MFDExt_OwnButtonOverrides = MFDExt_OwnButtonOverrides or {}

-- `ownPages` (optional, 4th arg) is a set of every page belonging to this
-- bay, ownPage included - added 2026-09-15 so a bay can chain through MORE
-- than two pages of its own with the same button (see MFDExt_ButtonB
-- below). Omitted, behavior is IDENTICAL to before this parameter existed:
-- `mine` reduces to `current == ownPage`, so every single-page bay (still
-- most of them) needs no changes.
--
-- The key move from the old two-page version: instead of "if current is
-- ownPage, maybe call an override; if current is ANY other page of ours,
-- unconditionally snap back to ownPage", `current`'s OWN override is
-- consulted whenever `current` belongs to THIS bay, regardless of which of
-- the bay's pages it is - so a bay can register a distinct override on
-- each of its pages and chain through them in order. A bay page with no
-- override registered on it (a dead end, or simply not implemented yet)
-- does nothing when the button is pressed there, same as ownPage always did.
--
-- This intentionally does NOT change cross-bay behavior: a DIFFERENT
-- button's own MFDExt_Redirect call never sees this bay's `ownPages` set
-- (each button only ever passes its own), so pressing button A while
-- sitting on any BATT page still takes the plain "jump to bay A" branch
-- below - only button B, the one bay B actually owns, ever consults BATT's
-- own override chain.
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

-- Button A ("SA" in our label row). Converted from its native per-page
-- softkey (9) to onClick+Lua on 2026-08-19, for uniformity with B/C/D/E -
-- the host fallback below replays the same softkey dispatch so every host
-- page keeps whatever native behavior it defined for softkey 9 (e.g. the
-- host's own home page reads a per-monitor persistent preference there,
-- not a fixed target - hardcoding one would have broken that).
function MFDExt_ButtonA(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_SA_Placeholder", function(id)
		fc.SendSoftkey(id, 9)
	end)
end

-- Button B ("BMS" in our label row - RealBattery's Battery Management
-- System, renamed from "BATT" 2026-08-27, same function-not-mod-name
-- convention as FADEC/SWC). Incidentally fixes an upstream typo:
-- the host's own onClick sends "MAS_JSI_BasicMFD_Graphs" (missing "B_"),
-- which was never a registered page name - our override supplies the
-- correct target as its "host" branch. See CLAUDE.md 2026-08-14.
--
-- Three-page cycle (2026-09-15, CLAUDE.md log 82): MFDExt_BATT_EPS (a
-- vessel-wide EPS summary, the NEW entry page - so every OTHER MFDExt page
-- and every host page still land here first) -> MFDExt_BATT (per-vessel
-- telemetry, the bay's original L1) -> MFDExt_BATT_Fleet (fleet view) ->
-- back to MFDExt_BATT_EPS. The three fc.SetPersistent calls that actually
-- drive the chain live in RealBattery's own script, keyed by page name in
-- the shared MFDExt_OwnButtonOverrides table - this function only has to
-- list which pages belong to this bay.
local MFDExt_BATT_Pages = {
	["MFDExt_BATT_EPS"] = true,
	["MFDExt_BATT"] = true,
	["MFDExt_BATT_Fleet"] = true,
}

function MFDExt_ButtonB(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_BATT_EPS", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_B_Graphs")
	end, MFDExt_BATT_Pages)
end

-- Button C ("KRAB" in our label row).
function MFDExt_ButtonC(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_KRAB_Placeholder", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_C_Targeting")
	end)
end

-- Button D ("KRILL" in our label row). Same conversion/rationale as A,
-- native softkey 12.
function MFDExt_ButtonD(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_KRILL_Placeholder", function(id)
		fc.SendSoftkey(id, 12)
	end)
end

-- Button E ("ILS" in our label row) - hosts NavInstruments, rescued from
-- its own dead RPM-only patches (see Pages/MFDExt_ILS.cfg). Native target
-- preserved when pressed from a host page, exactly like B/C.
function MFDExt_ButtonE(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_ILS", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_E_VesselView")
	end)
end

-- Button F ("CAS" in our label row) - our own textual fault-summary page
-- (WARNING/CAUTION/ADVISORY), backed by MFDExtCasModule (src/Cas/). Claimed
-- 2026-08-19, first bottom/top slot to move out of the shared Unclaimed
-- placeholder.
function MFDExt_ButtonF(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_CAS", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_F_EngineIgnitor")
	end)
end

-- G and the bottom row (R1-R7 / NAV-ORB-DOCK-DATA-CREW-RSRC-EXT) still
-- aren't real bays - all eight share one placeholder page (MFDExt_Unclaimed)
-- instead of leaking into the host's own ecosystem when pressed from
-- inside our world (see Pages/MFDExt_Unclaimed.cfg for why that mattered).
-- Native behavior outside our world is untouched, exactly like A-F: seven
-- of these eight are fixed onClick like B/C/E, R2 alone routes through
-- fc.SendSoftkey(17) like A/D (verified on the real source, 2026-08-19 -
-- never assume the scheme from another button, check every one).

function MFDExt_ButtonG(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_G_DPAI")
	end)
end

function MFDExt_ButtonR1(monitorID) -- NAV
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_1_Landing")
	end)
end

function MFDExt_ButtonR2(monitorID) -- ORB
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SendSoftkey(id, 17)
	end)
end

function MFDExt_ButtonR3(monitorID) -- DOCK
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_3_Docking")
	end)
end

function MFDExt_ButtonR4(monitorID) -- DATA
	MFDExt_Redirect(monitorID, "MFDExt_Unclaimed", function(id)
		fc.SetPersistent(id, "MAS_JSI_BasicMFD_4_ShipInfo")
	end)
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
