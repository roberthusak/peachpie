# Prepare the files needed to compile and run the tests

TOOL_DIR="./src/Tools/runtests_shell"
OUTPUT_DIR="$TOOL_DIR/bin"

cp src/Peachpie.Runtime/bin/Debug/netcoreapp1.0/Peachpie.Runtime.dll $OUTPUT_DIR
cp src/Peachpie.Library/bin/Debug/netcoreapp1.0/Peachpie.Library.dll $OUTPUT_DIR

# The location of the referenced libraries may differ and the compiler works properly only with absolute addresses
NUGET_DIR="$(readlink -f ~/.nuget/packages)"
awk "{print \"--reference:$NUGET_DIR/\" \$0}" $TOOL_DIR/references.rsp.tpl > $TOOL_DIR/references.rsp

COMPILE_PHP_DLL="./src/Peachpie.Compiler.Tools/bin/Debug/netcoreapp1.0/dotnet-compile-php.dll"
COMPILE_PHP="dotnet $COMPILE_PHP_DLL --temp-output:$OUTPUT_DIR --out:$OUTPUT_DIR/output.exe @$TOOL_DIR/common.rsp @$TOOL_DIR/references.rsp"

# Compile and run every PHP file in ./tests and check the output against the one from the PHP interpreter
for PHP_FILE in $(find ./tests -name *.php)
do
  echo "$PHP_FILE:"
  COMPILE_OUTPUT="$($COMPILE_PHP $PHP_FILE 2>&1)"
  if [ $PIPESTATUS != 0 ] ; then
    echo "Compilation error:"
    echo "$COMPILE_OUTPUT"
    FAILURE="FAILURE"
  else
    PHP_OUTPUT="$(php $PHP_FILE)"
    PEACH_OUTPUT="$(dotnet $OUTPUT_DIR/output.exe)"

    if [ "$PHP_OUTPUT" = "$PEACH_OUTPUT" ] ; then
      echo "OK"
    else
      echo "FAIL: (expected result | actual result)"
      diff -y -W 150 <(echo "$PHP_OUTPUT" ) <(echo "$PEACH_OUTPUT" )
      FAILURE="FAILURE"
    fi
  fi

  echo
done

# Fail if any of the tests failed
if [ $FAILURE ] ; then
  echo Tests failed
  exit -1
else
  echo Tests passed
  exit 0
fi