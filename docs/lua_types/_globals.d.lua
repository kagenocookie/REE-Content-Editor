---@type ReeLib.Workspace
env = {}

---@type ContentEditor.App.Windowing.EditorWindow
window = {}

json = {}

--- Takes a JSON string and turns it into a Lua value (usually a table). Returns nil on error.
--- @param json_str string
--- @return any
function json.load_string(json_str) return {} end

--- Takes a Lua value (usually a table) and turns it into a JSON string. Returns the "null" string on error. If unspecified, indent will default to -1.
--- @param value any
--- @param indent integer|nil
--- @return string
function json.dump_string(value, indent) return '' end

--- Loads a JSON file identified by filepath and returns it as a Lua value (usually a table). Returns nil if the file does not exist.
--- @param filepath string
--- @return any
function json.load_file(filepath) return {} end

--- Takes a Lua value (usually a table), and turns it into a JSON file identified as filepath. Returns true if the dump was successful, false otherwise. If unspecified, indent will default to 4
--- @param filepath string
--- @param value any
--- @param indent integer|nil
function json.dump_file(filepath, value, indent) end
