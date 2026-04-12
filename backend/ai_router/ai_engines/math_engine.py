import re
from rest_framework.response import Response

def process_math_and_conversions(raw_text_lower, session_key):
    """
    Intercepts architectural math and unit conversions.
    Returns a Django Response if a match is found, otherwise returns None.
    """
    
    # -----------------------------------------------------------------
    # 1. Slope / Ramp Calculator (Rise & Run)
    # -----------------------------------------------------------------
    slope_match = re.search(r"(?:calculate |what is the )?slope (?:for |with )?(?:a )?rise (?:of )?([\d,.]+) (?:and )?(?:a )?run (?:of )?([\d,.]+)", raw_text_lower)
    if slope_match:
        try:
            rise = float(slope_match.group(1).replace(",", ""))
            run = float(slope_match.group(2).replace(",", ""))
            if run > 0:
                percent = (rise / run) * 100
                ratio = run / rise
                msg = f"**Slope Calculation:**\nRise: {rise} | Run: {run}\nGradient: **{percent:,.2f}%** (or 1:{ratio:,.1f})"
                return Response({"message": msg, "session_key": session_key})
        except Exception:
            pass

    # -----------------------------------------------------------------
    # 2. Dimensional Area
    # -----------------------------------------------------------------
    area_dim_match = re.search(r"(?:calculate |what is the )?area of ([\d,.]+) ?(m|mm) (?:by|x|\*) ([\d,.]+) ?(m|mm)", raw_text_lower)
    if area_dim_match:
        try:
            w = float(area_dim_match.group(1).replace(",", ""))
            w_unit = area_dim_match.group(2)
            h = float(area_dim_match.group(3).replace(",", ""))
            h_unit = area_dim_match.group(4)
            
            w_m = w if w_unit == 'm' else w / 1000.0
            h_m = h if h_unit == 'm' else h / 1000.0
            area_sqm = w_m * h_m
            
            msg = f"**Area Calculation:**\n{w:,.0f}{w_unit} × {h:,.0f}{h_unit} = **{area_sqm:,.2f} sq/m**"
            return Response({"message": msg, "session_key": session_key})
        except Exception:
            pass

    # -----------------------------------------------------------------
    # 3. Enhanced Unit Conversions (Expanded for Architecture & Thai Land)
    # -----------------------------------------------------------------
    # Added rai, ngan, tarang wa, wa to the regex groups
    conversion_match = re.search(r"(?:convert|what is) ([\d,.]+) ?(sq/?mm|mm2|cu/?mm|mm3|in|inch|inches|\"|ft|feet|\'|sq/?m|m2|sq/?ft|ft2|rai|ngan|tarang wa|sq wa|wa) to (sq/?m|m2|sq/?ft|ft2|cu/?m|m3|mm|m|in|inch|inches|\"|ft|feet|\'|rai|ngan|tarang wa|sq wa|wa)", raw_text_lower)
    
    if conversion_match:
        try:
            raw_val = float(conversion_match.group(1).replace(",", ""))
            from_unit = conversion_match.group(2).replace("/", "").replace("\"", "in").replace("'", "ft")
            to_unit = conversion_match.group(3).replace("/", "").replace("\"", "in").replace("'", "ft")
            
            # Standardize Thai unit variations to make matching easier
            if from_unit in ["sq wa", "tarang wa"]: from_unit = "wa"
            if to_unit in ["sq wa", "tarang wa"]: to_unit = "wa"
            
            result = None
            
            # --- Standard Area (Metric <-> Imperial) ---
            if from_unit in ["sqm", "m2"] and to_unit in ["sqft", "ft2"]: result = raw_val * 10.7639
            elif from_unit in ["sqft", "ft2"] and to_unit in ["sqm", "m2"]: result = raw_val * 0.092903
            
            # --- Area / Volume (Micro to Macro) ---
            elif from_unit in ["sqmm", "mm2"] and to_unit in ["sqm", "m2"]: result = raw_val / 1000000.0
            elif from_unit in ["cumm", "mm3"] and to_unit in ["cum", "m3"]: result = raw_val / 1000000000.0
            
            # --- Linear (Metric <-> Imperial) ---
            elif from_unit in ["in", "inch", "inches"] and to_unit == "mm": result = raw_val * 25.4
            elif from_unit == "mm" and to_unit in ["in", "inch", "inches"]: result = raw_val / 25.4
            elif from_unit in ["ft", "feet"] and to_unit == "mm": result = raw_val * 304.8
            elif from_unit in ["ft", "feet"] and to_unit == "m": result = raw_val * 0.3048
            elif from_unit == "m" and to_unit in ["ft", "feet"]: result = raw_val * 3.28084
            
            # --- Thai Land Area (Rai / Ngan / Wa <-> Metric) ---
            # To Sqm
            elif from_unit == "rai" and to_unit in ["sqm", "m2"]: result = raw_val * 1600.0
            elif from_unit == "ngan" and to_unit in ["sqm", "m2"]: result = raw_val * 400.0
            elif from_unit == "wa" and to_unit in ["sqm", "m2"]: result = raw_val * 4.0
            # From Sqm
            elif from_unit in ["sqm", "m2"] and to_unit == "rai": result = raw_val / 1600.0
            elif from_unit in ["sqm", "m2"] and to_unit == "ngan": result = raw_val / 400.0
            elif from_unit in ["sqm", "m2"] and to_unit == "wa": result = raw_val / 4.0
            # Thai to Thai
            elif from_unit == "rai" and to_unit == "wa": result = raw_val * 400.0
            elif from_unit == "wa" and to_unit == "rai": result = raw_val / 400.0
            elif from_unit == "rai" and to_unit == "ngan": result = raw_val * 4.0
            elif from_unit == "ngan" and to_unit == "rai": result = raw_val / 4.0
                
            if result is not None:
                # Format Tarang Wa properly in the output string if 'wa' was used internally
                display_from = "tarang wa" if from_unit == "wa" else from_unit
                display_to = "tarang wa" if to_unit == "wa" else to_unit
                
                msg = f"**{raw_val:,.2f} {display_from}** = **{result:,.4f} {display_to}**"
                return Response({"message": msg, "session_key": session_key})
        except Exception:
            pass

    # -----------------------------------------------------------------
    # 3.5 Thai Compound Area Formatting (Sqm -> Rai, Ngan, Wa)
    # -----------------------------------------------------------------
    compound_match = re.search(r"(?:format|convert|what is) ([\d,.]+) ?(sq/?m|m2) (?:to|in|into) (?:thai|rai ngan wa|traditional)", raw_text_lower)
    if compound_match:
        try:
            sqm = float(compound_match.group(1).replace(",", ""))
            total_wa = sqm / 4.0
            
            rai = int(total_wa // 400)
            remainder_wa = total_wa % 400
            
            ngan = int(remainder_wa // 100)
            wa = remainder_wa % 100
            
            # Format the output cleanly, omitting zeros where appropriate
            parts = []
            if rai > 0: parts.append(f"**{rai}** Rai")
            if ngan > 0: parts.append(f"**{ngan}** Ngan")
            
            # Keep decimals for Tarang Wa if they exist, otherwise format as whole integer
            if wa > 0 or (rai == 0 and ngan == 0): 
                wa_str = f"{wa:,.2f}".rstrip('0').rstrip('.') if wa % 1 != 0 else f"{int(wa)}"
                parts.append(f"**{wa_str}** Tarang Wa")
                
            msg = f"**{sqm:,.2f} sq/m** is equivalent to:\n" + ", ".join(parts)
            return Response({"message": msg, "session_key": session_key})
        except Exception:
            pass

    # -----------------------------------------------------------------
    # 4. Advanced Math (Secured)
    # -----------------------------------------------------------------
    math_match = re.search(r"(?:what is|calculate) ([\d\s\+\-\*\/\.\(\)]+)", raw_text_lower)
    if math_match:
        equation = math_match.group(1).replace(",", "").strip()
        
        # Regex guarantees only math characters, but we still secure eval()
        if re.match(r'^[\d\s\+\-\*\/\.\(\)]+$', equation) and len(equation) > 1:
            try:
                # 💥 SECURITY FIX: Prevents code injection by emptying globals/locals
                result = eval(equation, {"__builtins__": {}}, {})
                msg = f"{equation.strip()} = **{result:,.2f}**"
                return Response({"message": msg, "session_key": session_key})
            except Exception:
                pass

    # 5. Fallthrough
    return None