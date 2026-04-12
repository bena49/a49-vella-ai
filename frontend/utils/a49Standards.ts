// utils/a49Standards.ts

// 1. VIEW TYPE TO KEYS MAPPING
const VIEW_TYPE_KEYS: Record<string, string[]> = {
    "Floor Plan": ["FL", "A1"],
    "Site Plan": ["SITE"],
    "Ceiling Plan": ["CP", "A5"],
    "Area Plan": ["AP"], 
    "Elevation": ["EL", "A2"],
    "Building Section": ["SE", "A3"],
    "Wall Section": ["WS", "A4"],
    "Detail View": ["D1", "A9"],
    "Schedule": ["SC", "A8", "AD", "AW"],
    "Enlarged Plan": ["PT", "A6"],
    "Vertical Circulation": ["ST", "A7"]
};

// 2. TEMPLATE MAPPING (Source of Truth)
const TEMPLATE_MAP: Record<string, Record<string, string[]>> = {
    "WV": {
        "FL": ["A49_WV_FLOOR PLAN"],
        "SITE": ["A49_WV_SITE PLAN"],
        "CP": ["A49_WV_REFLECTED CEILING PLAN"],
        "EL": ["A49_WV_ELEVATION"],
        "SE": ["A49_WV_BUILDING SECTION"],
        "WS": ["A49_WV_WALL SECTION"],
    },
    "PD": {
        "FL": ["A49_PD_FLOOR PLAN", "A49_PD_PRESENTATION PLAN"],
        "SITE": ["A49_PD_SITE PLAN"],
        "CP": ["A49_PD_REFLECTED CEILING PLAN"],
        "EL": ["A49_PD_ELEVATION"],
        "SE": ["A49_PD_BUILDING SECTION"],
        "WS": ["A49_PD_WALL SECTION"],
        "SC": ["A49_PD_DOOR & WINDOW"],
        "D1": ["A49_PD_DETAILS"],
        "AP": ["A49_PD_EIA AREA PLAN", "A49_PD_NFA AREA PLAN"],
    },
    "DD": {
        "FL": ["A49_DD_FLOOR PLAN", "A49_DD_PRESENTATION PLAN"],
        "SITE": ["A49_DD_SITE PLAN"],
        "CP": ["A49_DD_REFLECTED CEILING PLAN"],
        "EL": ["A49_DD_ELEVATION"],
        "SE": ["A49_DD_WALL SECTION"],
        "WS": ["A49_DD_WALL SECTION"],
        "SC": ["A49_DD_DOOR & WINDOW"],
        "D1": ["A49_DD_DETAILS"],
        "AP": ["A49_DD_EIA AREA PLAN", "A49_DD_NFA AREA PLAN"],
    },
    "CD": {
        "A1": [
            "A49_CD_A1_FLOOR PLAN",
            "A49_CD_A1_FLOOR PLAN_COLOR"
        ],
        "SITE": [
            "A49_CD_A1_SITE PLAN",
            "A49_CD_A1_SITE PLAN_COLOR"
        ],
        "A2": ["A49_CD_A2_ELEVATION", "A49_CD_A2_ELEVATION_COLOR"],
        "A3": ["A49_CD_A3_BUILDING SECTION", "A49_CD_A3_BUILDING SECTION_COLOR"],
        "A4": ["A49_CD_A4_WALL SECTION", "A49_CD_A4_DETAIL SECTION", "A49_CD_A4_WALL SECTION_COLOR"],
        "A5": [
            "A49_CD_A5_REFLECTED CEILING PLAN", 
            "A49_CD_A5_REFLECTED CEILING PLAN_COLOR"
        ],
        "A6": ["A49_CD_A6_PATTERNS PLAN", 'A49_CD_A6_INTERIOR ENLARGED PLAN', "A49_CD_A6_INTERIOR ELEVATION"],
        "A7": ["A49_CD_A7_VERTICAL CIRCULATION PLAN", "A49_CD_A7_VERTICAL CIRCULATION_SECTION"],
        
        "A8": ["A49_CD_A8_DOOR & WINDOW"],
        
        "A9": ["A49_CD_A9_DETAILS"]
    }
};

// 3. SCOPE BOX MAPPING
const SCOPE_BOX_MAP: Record<string, string[]> = {
    "A1": ["SB_PLAN_", "SB_PARTIAL_"],
    "FL": ["SB_PLAN_", "SB_PARTIAL_"], 
    "SITE": ["SB_PLAN_", "SB_PARTIAL_"],
    
    "A2": ["SB_BLDG_ELEV_"],
    "EL": ["SB_BLDG_ELEV_"],

    "A3": ["SB_BLDG_SECT_"],
    "SE": ["SB_BLDG_SECT_"],

    "A4": ["SB_WALL_SECT_"],
    "WS": ["SB_WALL_SECT_"],

    "A5": ["SB_PLAN_", "SB_PARTIAL_"],
    "CP": ["SB_PLAN_", "SB_PARTIAL_"],

    "A6": ["SB_ENLARGED_", "SB_INTR_ELEV_"],
    "PT": ["SB_ENLARGED_", "SB_INTR_ELEV_"],

    "A7": ["SB_PLAN_STAIR_", "SB_PLAN_LIFT_", "SB_PLAN_RAMP_"],
    "ST": ["SB_PLAN_STAIR_", "SB_PLAN_LIFT_", "SB_PLAN_RAMP_"],

    "A8": [], 
    "SC": [],
    "A9": [],
    "D1": [],
    
    "AP": ["SB_PLAN_", "SB_AREA_"] 
};

// =====================================================================
// 4. GLOBAL STANDARD DEFINITIONS
// =====================================================================

export const PROJECT_PHASE_MAP: Record<string, string> = {
    "WV": "00 - WORKING VIEW",
    "PD": "01 - PRE-DESIGN", 
    "DD": "02 - DESIGN DEVELOPMENT", 
    "CD": "03 - CONSTRUCTION DOCUMENTS"
};

export const SHEET_SET_MAP: Record<string, string> = {
    "A0": "A0_GENERAL INFORMATION", 
    "A1": "A1_FLOOR PLANS", 
    "A2": "A2_BUILDING ELEVATIONS", 
    "A3": "A3_BUILDING SECTIONS", 
    "A4": "A4_WALL SECTIONS", 
    "A5": "A5_CEILING PLANS",
    "A6": "A6_ENLARGED PLANS", 
    "A7": "A7_VERTICAL CIRCULATION", 
    "A8": "A8_DOOR AND WINDOW", 
    "A9": "A9_DETAILS"
};

// =====================================================================
// HELPER FUNCTIONS
// =====================================================================

export function getFilteredTemplates(stage: string, viewType: string, availableTemplates: string[]): string[] {
    if (!stage || !viewType) return availableTemplates;
    
    const stageKey = stage.toUpperCase();
    const stageMap = TEMPLATE_MAP[stageKey];
    
    if (!stageMap) return availableTemplates;

    const keys = VIEW_TYPE_KEYS[viewType] || [];
    
    // Find the list of Allowed Template Names for this Type
    let allowedNames: string[] = [];
    for (const k of keys) {
        if (stageMap[k]) {
            allowedNames = [...allowedNames, ...stageMap[k]];
        }
    }

    if (allowedNames.length === 0) return availableTemplates; // Fallback

    // Filter the real list from Revit
    return availableTemplates.filter(t => {
        const t_upper = t.toUpperCase();
        return allowedNames.some(allowed => t_upper.includes(allowed.toUpperCase()));
    });
}

export function getFilteredScopeBoxes(viewType: string, availableScopeBoxes: string[]): string[] {
    if (!viewType) return availableScopeBoxes;

    const keys = VIEW_TYPE_KEYS[viewType] || [];
    
    let allowedPrefixes: string[] = [];
    for (const k of keys) {
        if (SCOPE_BOX_MAP[k]) {
            allowedPrefixes = [...allowedPrefixes, ...SCOPE_BOX_MAP[k]];
        }
    }

    // Explicit Empty check
    const hasMatch = keys.some(k => Object.prototype.hasOwnProperty.call(SCOPE_BOX_MAP, k));
    if (hasMatch && allowedPrefixes.length === 0) {
        return []; 
    }
    
    if (allowedPrefixes.length === 0) return availableScopeBoxes;

    return availableScopeBoxes.filter(sb => {
        const sb_upper = sb.toUpperCase();
        return allowedPrefixes.some(prefix => sb_upper.includes(prefix.toUpperCase()));
    });
}