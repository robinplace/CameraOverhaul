#!zsh

set -e
cd "$(git rev-parse --show-toplevel)"

print_help() {
	cat <<help
usage: ./t [options] [command]

options:
  -h, --help  print this help message

commands:
	build	   build all packages
	watch	   watch build all packages
	pack	   zip all packages
	start	   start timberborn
	restart	 restart timberborn, if it is running
	kill	   kill timberborn
help
}
zparseopts -D -- h=help -help=help
if [[ -v help[1] ]]; then print_help; exit; fi

case "$1" in
	"build")
		for dir in ./*/manifest.json(N); do
			mod="${dir:h}"
			pushd "./$mod"
			dotnet build -p:Mod="$mod" || exit 1
			popd
		done
	;;"watch")
		free() { kill $(jobs -p) || true; }
		trap free EXIT
		for dir in ./*/manifest.json(N); do
			mod="${dir:h}"
			pushd "./$mod"
			dotnet watch build -- -p:Mod="$mod" &
			popd
		done
		wait
	;;"pack")
		pushd "./build"
		for dir in ./*/manifest.json(N); do
			mod="${dir:h}"
			pushd "./$mod"
			zip "../$mod.zip" ./*
			popd
		done
		popd
	;;"start")
		echo "starting"
		/Applications/Steam.app/Contents/MacOS/steam_osx -applaunch 1062090 -skipModManager
	;;"kill")
		echo "killing"
		killall Timberborn
	;;"restart")
		echo "restarting"
		./t kill && ./t start || true
	;;*)
		print_help && exit 1
esac
