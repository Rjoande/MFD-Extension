-- Shared navigation logic for the MFD Extended additive branch.
--
-- Most of A-G and STBY on MAS_JSI_BasicMFD are NOT per-page softkeys -
-- they're wired once, prop-wide, straight to a fixed page via onClick
-- (verified on the real source, 2026-08-09/14). To let them do double duty
-- (native destination from host pages, ours from ours) the decision has to
-- live here, checking which page is currently showing.
--
-- Two buttons (A, D) are the exception: they route through fc.SendSoftkey
-- instead, so they get a plain per-page `softkey =` binding on
-- Pages/MFDExt_Stby.cfg instead - no Lua needed for those.

local MFDExt_OwnPages = {
	["MFDExt_Stby"] = true,
	["MFDExt_SA_Placeholder"] = true,
	["MFDExt_BATT_Placeholder"] = true,
	["MFDExt_KRAB_Placeholder"] = true,
	["MFDExt_KRILL_Placeholder"] = true,
	["MFDExt_ILS"] = true,
}

local function MFDExt_Redirect(monitorID, ourTarget, hostTarget)
	local current = fc.GetPersistent(monitorID)
	if MFDExt_OwnPages[current] then
		fc.SetPersistent(monitorID, ourTarget)
	else
		fc.SetPersistent(monitorID, hostTarget)
	end
end

-- Button B ("BATT" in our label row). Incidentally fixes an upstream typo:
-- the host's own onClick sends "MAS_JSI_BasicMFD_Graphs" (missing "B_"),
-- which was never a registered page name - our override supplies the
-- correct target as its "host" branch. See CLAUDE.md 2026-08-14.
function MFDExt_ButtonB(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_BATT_Placeholder", "MAS_JSI_BasicMFD_B_Graphs")
end

-- Button C ("KRAB" in our label row).
function MFDExt_ButtonC(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_KRAB_Placeholder", "MAS_JSI_BasicMFD_C_Targeting")
end

-- Button E ("ILS" in our label row) - hosts NavInstruments, rescued from
-- its own dead RPM-only patches (see Pages/MFDExt_ILS.cfg). Native target
-- preserved when pressed from a host page, exactly like B/C.
function MFDExt_ButtonE(monitorID)
	MFDExt_Redirect(monitorID, "MFDExt_ILS", "MAS_JSI_BasicMFD_E_VesselView")
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
